using System.Net;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class MeshWireTests
{
    public static void Run(bool sockets)
    {
        ReliableLossAndOrdering();
        FullMesh();
        NoncesAndMalformedPackets();
        QueueAndTimeoutBounds();
        CompactDatagramsRejectStaleConnections();
        if (sockets)
            RealUdp();
    }

    private static void CompactDatagramsRejectStaleConnections()
    {
        var network = new Network(117, 0, 0, 0);
        var a = network.Add("a");
        var b = network.Add("b");
        a.Send("b", [1], true);
        for (int i = 0; i < 30; i++)
            network.Step(10);
        Check(b.Receive(out _), "Compact datagram fixture did not establish nonces");
        a.Send("b", Payload(6, 1200), false);
        byte[] captured = network.Packets.Single().Data.ToArray();
        Check(captured.Length == 1221, "Small unreliable messages still carry reliable fragmentation fields");
        network.Step(10);
        Check(
            b.Receive(out var received) && received.Data.SequenceEqual(Payload(6, 1200)),
            "Compact datagram changed its payload"
        );
        b.Process("unrelated", captured);
        Check(!b.Receive(out _), "Compact datagram bypassed endpoint validation");
        byte[] wrongRecipient = captured.ToArray();
        wrongRecipient[13] ^= 1;
        b.Process("a", wrongRecipient);
        Check(!b.Receive(out _), "Compact datagram bypassed recipient nonce validation");
        for (int length = 0; length <= 21; length++)
            b.Process("a", captured.AsSpan(0, length));
        Check(!b.Receive(out _), "Truncated compact datagram reached delivery");
        b = network.Add("b");
        b.Process("a", captured);
        Check(!b.Receive(out _), "An old compact datagram reached a restarted receiver");
        b.Send("a", [2], true);
        for (int i = 0; i < 30; i++)
            network.Step(10);
        a.Send("b", [3], false);
        network.Step(10);
        Check(
            b.Receive(out var fresh) && fresh.Data.SequenceEqual(new byte[] { 3 }),
            "Compact datagrams did not recover after nonce negotiation"
        );
    }

    private static void ReliableLossAndOrdering()
    {
        var network = new Network(371, 0.3, 0.2, 180);
        var a = network.Add("a");
        var b = network.Add("b");
        for (int i = 0; i < 256; i++)
        {
            a.Send("b", Payload(i, i % 9 == 0 ? 8192 : 700 + i), true);
            b.Send("a", Payload(i + 1000, i % 7 == 0 ? 4200 : 50 + i), true);
        }
        var fromA = new List<byte[]>();
        var fromB = new List<byte[]>();
        for (int tick = 0; tick < 1400 && (fromA.Count < 256 || fromB.Count < 256); tick++)
        {
            network.Step(10);
            Drain(a, fromB);
            Drain(b, fromA);
        }
        Check(fromA.Count == 256 && fromB.Count == 256, "Reliable UDP did not recover all queued controls under loss");
        for (int i = 0; i < 256; i++)
        {
            Check(
                fromA[i].SequenceEqual(Payload(i, i % 9 == 0 ? 8192 : 700 + i)),
                "UDP controls lost order or reassembled incorrectly"
            );
            Check(
                fromB[i].SequenceEqual(Payload(i + 1000, i % 7 == 0 ? 4200 : 50 + i)),
                "Reverse UDP controls lost order or reassembled incorrectly"
            );
        }
        Check(a.Statistics.Retried > 0 && b.Statistics.Retried > 0, "UDP loss fixture did not exercise retries");
        Check(!a.TakeDisconnected(out _) && !b.TakeDisconnected(out _), "Recoverable loss disconnected a UDP peer");
        for (int i = 0; i < 100; i++)
            network.Step(10);
        Check(!a.Receive(out _) && !b.Receive(out _), "Reliable UDP duplicate was delivered twice");
        Check(network.MaximumPacketLength <= 1232, "UDP fragmented control exceeded minimum IPv6 datagram budget");
    }

    private static void FullMesh()
    {
        var network = new Network(551, 0.1, 0.05, 100);
        var nodes = Enumerable.Range(0, 8).Select(index => network.Add(index.ToString())).ToArray();
        for (int sender = 0; sender < nodes.Length; sender++)
        {
            nodes[sender]
                .SetPeers(
                    Enumerable.Range(0, 8).Where(peer => peer != sender).Select(peer => peer.ToString()).ToArray()
                );
            for (int recipient = 0; recipient < nodes.Length; recipient++)
                if (recipient != sender)
                    for (int i = 0; i < 20; i++)
                        nodes[sender].Send(recipient.ToString(), Payload(sender * 20 + i, 32), true);
        }
        var counts = new int[8, 8];
        for (int tick = 0; tick < 600; tick++)
        {
            network.Step(10);
            for (int recipient = 0; recipient < nodes.Length; recipient++)
                while (nodes[recipient].Receive(out var message))
                {
                    int sender = int.Parse(message.Source);
                    int index = counts[recipient, sender]++;
                    Check(
                        message.Data.SequenceEqual(Payload(sender * 20 + index, 32)),
                        "Mesh link delivered another peer's control or reordered it"
                    );
                }
        }
        for (int recipient = 0; recipient < 8; recipient++)
        for (int sender = 0; sender < 8; sender++)
            Check(
                counts[recipient, sender] == (recipient == sender ? 0 : 20),
                "Not all direct UDP mesh links completed"
            );
        network.Nodes.Remove("0");
        nodes[1].Send("2", Payload(42, 123), true);
        for (int i = 0; i < 100; i++)
            network.Step(10);
        Check(
            nodes[2].Receive(out var direct) && direct.Source == "1" && direct.Data.SequenceEqual(Payload(42, 123)),
            "UDP guest links depended on the host forwarding"
        );
    }

    private static void NoncesAndMalformedPackets()
    {
        var network = new Network(1, 0, 0, 0);
        var a = network.Add("a");
        var b = network.Add("b");
        a.Send("b", Payload(1, 32), true);
        byte[] oldIntroduction = network.Packets.Single().Data.ToArray();
        for (int i = 0; i < 30; i++)
            network.Step(10);
        Check(b.Receive(out _), "Reliable UDP nonce negotiation failed");
        a.Send("b", Payload(2, 32), true);
        byte[] oldAddressedPacket = network.Packets.First(packet => packet.Source == "a").Data.ToArray();
        for (int i = 0; i < 30; i++)
            network.Step(10);
        Check(b.Receive(out _), "Reliable UDP second send failed");
        b = network.Add("b");
        b.Process("a", oldAddressedPacket);
        b.Process("a", oldIntroduction);
        Check(!b.Receive(out _), "Previous UDP connection payload entered a restarted receiver");
        for (int i = 0; i < 30; i++)
            network.Step(10);
        a.Send("b", Payload(3, 32), true);
        for (int i = 0; i < 30; i++)
            network.Step(10);
        Check(
            b.Receive(out var fresh) && fresh.Data.SequenceEqual(Payload(3, 32)),
            "UDP did not recover after nonce replacement"
        );
        a.Send("b", Payload(4, 32), false);
        for (int i = 0; i < 10; i++)
            network.Step(10);
        Check(
            b.Receive(out var unreliable) && unreliable.Data.SequenceEqual(Payload(4, 32)),
            "UDP unreliable stream was blocked by reliable ordering"
        );
        a.SetPeers(["b"]);
        a.SetPeers([]);
        a.SetPeers(["b"]);
        a.Send("b", Payload(5, 32), true);
        for (int i = 0; i < 30; i++)
            network.Step(10);
        Check(
            b.Receive(out var readmitted) && readmitted.Data.SequenceEqual(Payload(5, 32)),
            "UDP peer removal and re-admission reused a stale reliable sequence"
        );
        var random = new Random(99);
        for (int i = 0; i < 1000; i++)
        {
            byte[] malformed = new byte[random.Next(0, 1600)];
            random.NextBytes(malformed);
            b.Process("a", malformed);
        }
        Check(!b.Receive(out _) && b.Statistics.Ignored >= 1000, "Malformed UDP packets reached lobby delivery");
    }

    private static void QueueAndTimeoutBounds()
    {
        long now = 0;
        var bounded = new DatagramReliability((_, _) => { }, () => now);
        for (int i = 0; i < 513; i++)
            bounded.Send("absent", [1], true);
        Check(
            bounded.TakeDisconnected(out string address) && address == "absent",
            "UDP reliable queue overflow was silent"
        );
        Check(bounded.Statistics.Backpressure == 1, "UDP queue overflow was not counted");
        bounded.Send("timeout", [1], true);
        now = 15000;
        bounded.Poll();
        Check(
            bounded.TakeDisconnected(out address) && address == "timeout",
            "UDP abandoned reliable send did not time out"
        );
    }

    private static void RealUdp()
    {
        using var a = new UdpWire(new IPEndPoint(IPAddress.Loopback, 0));
        using var b = new UdpWire(new IPEndPoint(IPAddress.Loopback, 0));
        a.SetPeers([b.LocalAddress]);
        b.SetPeers([a.LocalAddress]);
        a.Send(b.LocalAddress, Payload(18, 8192), true);
        b.Send(a.LocalAddress, Payload(29, 1536), true);
        byte[]? atA = null;
        byte[]? atB = null;
        for (int i = 0; i < 1000 && (atA == null || atB == null); i++)
        {
            a.Poll();
            b.Poll();
            if (a.Receive(out var receivedA))
                atA = receivedA.Data;
            if (b.Receive(out var receivedB))
                atB = receivedB.Data;
            if (atA == null || atB == null)
                Thread.Sleep(1);
        }
        Check(atA != null && atA.SequenceEqual(Payload(29, 1536)), "Real UDP reverse reliable message failed");
        Check(atB != null && atB.SequenceEqual(Payload(18, 8192)), "Real UDP maximum reliable message failed");
        Check(a.IsConnected(b.LocalAddress) && b.IsConnected(a.LocalAddress), "Real UDP links did not establish");
    }

    private static byte[] Payload(int sequence, int length)
    {
        var random = new Random(sequence);
        var payload = new byte[length];
        random.NextBytes(payload);
        return payload;
    }

    private static void Drain(DatagramReliability node, List<byte[]> destination)
    {
        while (node.Receive(out var message))
            destination.Add(message.Data);
    }

    private sealed class Network(int seed, double loss, double duplication, int jitter)
    {
        private readonly Random random = new(seed);
        public readonly Dictionary<string, DatagramReliability> Nodes = new();
        public readonly List<Packet> Packets = new();
        public long Now;
        public int MaximumPacketLength;

        public DatagramReliability Add(string address)
        {
            var node = new DatagramReliability((destination, data) => Send(address, destination, data), () => Now);
            Nodes[address] = node;
            return node;
        }

        private void Send(string source, string destination, byte[] data)
        {
            MaximumPacketLength = Math.Max(MaximumPacketLength, data.Length);
            if (random.NextDouble() < loss)
                return;
            Packets.Add(new(source, destination, data.ToArray(), Now + random.Next(jitter + 1)));
            if (random.NextDouble() < duplication)
                Packets.Add(new(source, destination, data.ToArray(), Now + random.Next(jitter + 1)));
        }

        public void Step(int milliseconds)
        {
            Now += milliseconds;
            var ready = Packets.Where(packet => packet.Due <= Now).OrderBy(packet => packet.Due).ToArray();
            Packets.RemoveAll(packet => packet.Due <= Now);
            foreach (var packet in ready)
                if (Nodes.TryGetValue(packet.Destination, out var receiver))
                    receiver.Process(packet.Source, packet.Data);
            foreach (var node in Nodes.Values)
                node.Poll();
        }
    }

    internal readonly record struct Packet(string Source, string Destination, byte[] Data, long Due);
}
