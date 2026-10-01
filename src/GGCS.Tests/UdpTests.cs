using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace GGCS.Tests;

internal static class UdpTests
{
    public static void Run()
    {
        using var firstTransport = new UdpTransport();
        using var secondTransport = new UdpTransport();
        firstTransport.AddPeer(1, secondTransport.Address);
        secondTransport.AddPeer(0, firstTransport.Address);
        VerifyDatagramOwnership(firstTransport, secondTransport);
        firstTransport.DropEvery = 17;
        secondTransport.DropEvery = 19;

        const int target = 480;
        var clock = new TestClock();
        var options = new SessionOptions
        {
            FramesPerSecond = 120,
            InputDelay = 2,
            MaxPredictionFrames = 24,
            HistoryFrames = 96,
            RetryMilliseconds = 20,
            ChecksumInterval = 30,
        };
        Player[] players = [new(0, 0), new(1, 0), new(2, 1), new(3, 1)];
        TestGame[] games = [new(), new()];
        P2PSession<int, ulong>[] sessions =
        [
            new(0x55445054455354, 0, players, games[0], new IntCodec(), firstTransport, options, clock),
            new(0x55445054455354, 1, players, games[1], new IntCodec(), secondTransport, options, clock),
        ];
        var expected = ReferenceTimeline(target, options.InputDelay);
        int[] verified = [-1, -1];
        var timer = Stopwatch.StartNew();
        for (int tick = 0; tick < 10000 && timer.Elapsed.TotalSeconds < 10; tick++)
        {
            clock.NowMilliseconds = tick * 1000L / options.FramesPerSecond;
            for (int peer = 0; peer < sessions.Length; peer++)
            {
                var session = sessions[peer];
                session.Poll();
                if (session.CurrentFrame < target)
                {
                    int frame = session.CurrentFrame;
                    session.AdvanceFrame([SessionRig.Input(frame, peer * 2), SessionRig.Input(frame, peer * 2 + 1)]);
                }
            }
            foreach (var session in sessions)
                session.Poll();

            for (int peer = 0; peer < sessions.Length; peer++)
            {
                var session = sessions[peer];
                while (session.TryGetEvent(out var item))
                {
                    Check.True(
                        item.Kind
                            is not (
                                SessionEventKind.ProtocolError
                                or SessionEventKind.DesyncDetected
                                or SessionEventKind.Disconnected
                                or SessionEventKind.Excluded
                            ),
                        $"UDP session event: {item}"
                    );
                }
                for (int frame = verified[peer] + 1; frame <= session.ConfirmedFrame; frame++)
                {
                    Check.True(
                        session.TryGetChecksum(frame + 1, out ulong checksum),
                        "UDP confirmed checksum is retained"
                    );
                    Check.Equal(expected[frame], checksum, "UDP confirmed state matches direct four-player simulation");
                }
                verified[peer] = session.ConfirmedFrame;
            }
            if (verified.All(frame => frame == target - 1))
                break;
        }

        for (int peer = 0; peer < sessions.Length; peer++)
        {
            Check.Equal(
                target,
                sessions[peer].CurrentFrame,
                "UDP session reaches target with two local inputs per peer"
            );
            Check.Equal(target - 1, sessions[peer].ConfirmedFrame, "UDP retransmission eventually confirms all frames");
            Check.Equal(expected[^1], games[peer].Value, "Final UDP game state matches uninterrupted reference");
            var stats = sessions[peer].GetNetworkStats(1 - peer);
            Check.True(
                stats.PacketsReceived > 0 && stats.PacketsSent > 0,
                "Session exchanges real UDP datagrams in both directions"
            );
            sessions[peer].Close();
        }
        Check.True(
            firstTransport.DroppedPackets > 0 && secondTransport.DroppedPackets > 0,
            "Loopback test exercises packet loss in both directions"
        );
        Console.WriteLine(
            $"GGCS UDP: 2 peers/4 players, {target} confirmed ticks, {target * 2} hashes verified; "
                + $"{firstTransport.DroppedPackets + secondTransport.DroppedPackets} datagrams dropped deliberately."
        );
    }

    private static ulong[] ReferenceTimeline(int count, int inputDelay)
    {
        var baseline = new TestGame();
        var inputs = new PlayerInput<int>[4];
        var hashes = new ulong[count];
        for (int frame = 0; frame < count; frame++)
        {
            for (int player = 0; player < inputs.Length; player++)
                inputs[player] = new PlayerInput<int>(
                    frame < inputDelay ? 0 : SessionRig.Input(frame - inputDelay, player),
                    InputStatus.Confirmed
                );
            baseline.AdvanceFrame(frame, inputs, false);
            hashes[frame] = baseline.Value;
        }
        return hashes;
    }

    private static void VerifyDatagramOwnership(UdpTransport sender, UdpTransport receiver)
    {
        byte[] source = [1, 2, 3, 4];
        sender.Send(1, source);
        source.AsSpan().Fill(99);
        Datagram first = Receive(receiver);
        sender.Send(1, [5, 6, 7, 8]);
        Datagram second = Receive(receiver);
        Check.Equal(0, first.PeerId, "Transport maps a loopback address to the sending peer");
        Check.True(
            first.Data.Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "Sent and received datagram storage is independent of reused buffers"
        );
        Check.True(
            second.Data.Span.SequenceEqual(new byte[] { 5, 6, 7, 8 }),
            "Next datagram preserves its own contents"
        );
    }

    private static Datagram Receive(UdpTransport receiver)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 2)
        {
            if (receiver.TryReceive(out var packet))
                return packet;
            Thread.Yield();
        }
        throw new TimeoutException("Loopback UDP datagram was not delivered within two seconds.");
    }

    private sealed class UdpTransport : ITransport, IDisposable
    {
        private readonly Socket socket;
        private readonly Dictionary<int, IPEndPoint> addresses = new();
        private readonly byte[] receiveBuffer = new byte[65507];
        private int sendAttempts;
        public IPEndPoint Address => (IPEndPoint)socket.LocalEndPoint!;
        public int DropEvery { get; set; }
        public int DroppedPackets { get; private set; }

        public UdpTransport()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                socket.Blocking = false;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public void AddPeer(int peerId, IPEndPoint address) => addresses.Add(peerId, address);

        public void Send(int peerId, ReadOnlySpan<byte> packet)
        {
            sendAttempts++;
            if (DropEvery > 0 && sendAttempts % DropEvery == 0)
            {
                DroppedPackets++;
                return;
            }
            int sent = socket.SendTo(packet, SocketFlags.None, addresses[peerId]);
            if (sent != packet.Length)
                throw new IOException("The loopback socket did not send the entire datagram.");
        }

        public bool TryReceive(out Datagram datagram)
        {
            datagram = default;
            if (!socket.Poll(0, SelectMode.SelectRead))
                return false;
            EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            int count;
            try
            {
                count = socket.ReceiveFrom(receiveBuffer, SocketFlags.None, ref sender);
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.WouldBlock)
            {
                return false;
            }
            foreach (var (peerId, address) in addresses)
            {
                if (address.Equals(sender))
                {
                    datagram = new Datagram(peerId, receiveBuffer.AsMemory(0, count).ToArray());
                    return true;
                }
            }
            throw new InvalidOperationException($"Unexpected loopback sender: {sender}");
        }

        public void Dispose() => socket.Dispose();
    }
}
