using FrogSmashers.Core;
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
        using var sa = new NetworkSession(a, new SessionConfig(slots, 0, "test", a, 1), network.Endpoint(0));
        using var sb = new NetworkSession(b, new SessionConfig(slots, 1, "test", b, 1), network.Endpoint(1));
        network.Inject(1, 0, new byte[3]);
        sa.Poll();
        Check(sa.RejectedPackets == 1, "Truncated packet was not rejected");
        NetworkSessionTests.Synchronize(network, [sa, sb]);
        for (int tick = 0; tick < 200; tick++)
        {
            network.Advance();
            sa.Poll();
            sb.Poll();
            if (tick == 70)
                b.Players[0].Score++;
            sa.TryAdvance([Input(a.TickNumber, 0)]);
            sb.TryAdvance([Input(b.TickNumber, 1)]);
            if (sa.Error != null || sb.Error != null)
                break;
        }
        Check((sa.Error ?? sb.Error ?? "").Contains("Desync"), "Confirmed hash mismatch was not detected");

        var mismatch = new SimulatedNetwork(2, 1, 1, 0, 0, 0);
        var c = MakeWorld(2);
        var d = MakeWorld(2);
        using var sc = new NetworkSession(c, new SessionConfig(slots, 0, "one", c, 2), mismatch.Endpoint(0));
        using var sd = new NetworkSession(d, new SessionConfig(slots, 1, "two", d, 2), mismatch.Endpoint(1));
        for (int tick = 0; tick < 20 && sc.Error == null && sd.Error == null; tick++)
        {
            mismatch.Advance();
            sc.Poll();
            sd.Poll();
        }
        Check(
            (sc.Error ?? sd.Error ?? "").Contains("configuration", StringComparison.OrdinalIgnoreCase),
            "Mismatched content was not refused"
        );
        Console.WriteLine("GGCS packet validation and cross-peer desync detection passed");
    }

    public static void PacketStructure()
    {
        Guid core = Guid.Parse("1d012f4e-5128-427f-9999-576874dc999c");
        Guid network = Guid.Parse("fb6938c1-07c0-4e87-b06f-4a4ae598b50f");
        string fingerprint = NetworkBuild.ContentFingerprint("content", core, network, Guid.Empty);
        Check(
            fingerprint != NetworkBuild.ContentFingerprint("content", core, network, core),
            "Changing only the rollback library invalidates the admission fingerprint"
        );
        var codec = new RollbackInputCodec();
        byte[] bytes = new byte[codec.Size];
        var input = new RollbackInput(new(-1, 1, InputButtons.Jump | InputButtons.Strafe), 3, -1, 1);
        codec.Encode(input, bytes);
        Check(codec.Decode(bytes) == input, "Gameplay and lobby commands round-trip exactly");
        foreach (int index in new[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            byte[] invalid = bytes.ToArray();
            invalid[index] = 127;
            bool rejected = false;
            try
            {
                codec.Decode(invalid);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Check(rejected, $"Invalid rollback input field {index} was accepted");
        }

        int[][] slots =
        [
            [0],
            [1],
        ];
        var receive = new ManualTransport();
        var world = MakeWorld(2);
        using var session = new NetworkSession(world, new SessionConfig(slots, 0, "packets", world, 2), receive);
        foreach (var packet in new[] { Array.Empty<byte>(), new byte[3], new byte[1201] })
        {
            receive.Incoming.Enqueue(new(1, packet));
            session.Poll();
            Check(session.Error == null, "Malformed packet incorrectly killed session");
        }
        receive.Incoming.Enqueue(new(15, new byte[100]));
        session.Poll();
        Check(session.RejectedPackets == 4, "Malformed or unadmitted packet was accepted");

        var failureWire = new ManualTransport { Error = "disconnected" };
        var other = MakeWorld(2);
        using var disconnected = new NetworkSession(
            other,
            new SessionConfig(slots, 0, "failure", other, 2),
            failureWire
        );
        disconnected.Poll();
        Check(
            disconnected.IsTransportFailure && disconnected.Error == "disconnected",
            "Transport errors need explicit disconnect classification"
        );
        Console.WriteLine("Explicit rollback input codec, packet bounds and disconnect classification passed");
    }

    private sealed class ManualTransport : IPeerTransport
    {
        public Queue<Datagram> Incoming { get; } = new();
        public string? Error { get; set; }

        public void Poll() { }

        public void Send(int peer, ReadOnlySpan<byte> data) { }

        public bool TryReceive(out Datagram datagram) => Incoming.TryDequeue(out datagram);

        public void Dispose() { }
    }
}
