using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class NetworkProtocolTests
{
    public static void PacketValidationAndDesync()
    {
        var network = new SimulatedNetwork(2, 1, 1, 0, 0, 0);
        int[][] slots =
        [
            [0],
            [1],
        ];
        var a = MakeWorld(2);
        var b = MakeWorld(2);
        using var sa = new NetworkSession(a, new SessionConfig(slots, 0, "test", a), network.Endpoint(0));
        using var sb = new NetworkSession(b, new SessionConfig(slots, 1, "test", b), network.Endpoint(1));
        network.Inject(1, 0, new byte[3]);
        sa.Poll();
        Check(sa.RejectedPackets == 1, "Truncated packet was not rejected");
        for (int tick = 0; tick < 160; tick++)
        {
            network.Advance();
            sa.Poll();
            sb.Poll();
            if (tick == 70)
            {
                b.Players[0].Score++;
            }

            sa.TryAdvance([Input(a.TickNumber, 0)]);
            sb.TryAdvance([Input(b.TickNumber, 1)]);
            if (sa.Error != null || sb.Error != null)
            {
                break;
            }
        }

        Check((sa.Error ?? sb.Error ?? "").Contains("Desync"), "Confirmed hash mismatch was not detected");
        var mismatch = new SimulatedNetwork(2, 1, 1, 0, 0, 0);
        var c = MakeWorld(2);
        var d = MakeWorld(2);
        using var sc = new NetworkSession(c, new SessionConfig(slots, 0, "one", c), mismatch.Endpoint(0));
        using var sd = new NetworkSession(d, new SessionConfig(slots, 1, "two", d), mismatch.Endpoint(1));
        sd.TryAdvance([default]);
        mismatch.Advance();
        sc.Poll();
        Check(sc.Error?.Contains("does not match") == true, "Mismatched content was not refused");
        Console.WriteLine("Packet validation and cross-peer desync detection passed");
    }

    public static void PacketStructure()
    {
        int[][] slots =
        [
            [0],
            [1],
        ];
        var a = MakeWorld(2);
        var b = MakeWorld(2);
        var receive = new ManualTransport();
        var send = new ManualTransport();
        using var sa = new NetworkSession(a, new SessionConfig(slots, 0, "packets", a), receive);
        using var sb = new NetworkSession(b, new SessionConfig(slots, 1, "packets", b), send);
        sb.TryAdvance([default]);
        var valid = send.Last!;
        var invalid = new List<byte[]>();
        var axis = valid.ToArray();
        axis[87] = 2;
        invalid.Add(axis);
        var high = valid.ToArray();
        high[90] = 255;
        invalid.Add(high);
        var buttons = valid.ToArray();
        buttons[89] = 16;
        invalid.Add(buttons);
        var identity = valid.ToArray();
        identity[37] = 0;
        invalid.Add(identity);
        var future = valid.ToArray();
        BitConverter.GetBytes(long.MaxValue).CopyTo(future, 78);
        invalid.Add(future);
        var ack = valid.ToArray();
        BitConverter.GetBytes(long.MaxValue).CopyTo(ack, 46);
        invalid.Add(ack);
        invalid.Add(valid[..^1]);
        invalid.Add([.. valid, (byte)0]);
        invalid.Add(new byte[1201]);
        foreach (var packet in invalid)
        {
            receive.Incoming.Enqueue(new(1, packet));
            sa.Poll();
            Check(sa.Error == null, "Malformed packet incorrectly killed session");
        }

        Check(sa.RejectedPackets == invalid.Count, "Malformed packet was accepted");
        receive.Incoming.Enqueue(new(1, valid));
        sa.Poll();
        Check(sa.Error == null, "Valid packet rejected");
        var changed = valid.ToArray();
        changed[87] = 1;
        receive.Incoming.Enqueue(new(1, changed));
        sa.Poll();
        Check(sa.Error?.Contains("already submitted") == true, "Peer was allowed to revise committed input");
        Check(!sa.IsTransportFailure, "Protocol error misclassified as a disconnect");
        var failureWire = new ManualTransport { Error = "disconnected" };
        var c = MakeWorld(2);
        using var disconnected = new NetworkSession(c, new SessionConfig(slots, 0, "failure", c), failureWire);
        disconnected.Poll();
        Check(
            disconnected.IsTransportFailure && disconnected.Error == "disconnected",
            "Transport errors need explicit disconnect classification"
        );
        Console.WriteLine("Packet bounds, slot identity, input validation and immutable input checks passed");
    }

    private sealed class ManualTransport : IPeerTransport
    {
        public Queue<Datagram> Incoming { get; } = new();
        public byte[]? Last { get; private set; }
        public string? Error { get; set; }

        public void Poll() { }

        public void Send(int peer, ReadOnlySpan<byte> data) => Last = data.ToArray();

        public bool TryReceive(out Datagram datagram) => Incoming.TryDequeue(out datagram);

        public void Dispose() { }
    }
}
