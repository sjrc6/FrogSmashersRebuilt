using FrogSmashers.Core;
using FrogSmashers.Network;
using Steamworks;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class SteamLegacyWireTests
{
    public static void Run()
    {
        foreach (var transport in Enum.GetValues<SteamTransport>())
            Check(
                SteamTransportMetadata.Parse(SteamTransportMetadata.Name(transport)) == transport,
                "Guest selects the transport advertised by the host"
            );
        foreach (string invalid in new[] { "", "unknown", "LEGACY" })
        {
            bool rejected = false;
            try
            {
                SteamTransportMetadata.Parse(invalid);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Check(rejected, "Missing or unsupported transport is rejected before connecting");
        }
        AdmissionAndPackets();
        BackpressureAndFailure();
        FullMeshLobby();
    }

    private static void AdmissionAndPackets()
    {
        var api = new FakeApi(1);
        using var host = new SteamLegacyWire(api, true, 1);
        api.Requests.Enqueue(2);
        api.Requests.Enqueue(99);
        api.Requests.Enqueue(1);
        host.Poll();
        Check(api.Accepted.SetEquals([2]), "Legacy host accepts only other current lobby members");
        Check(api.Closed.Contains(99), "Legacy requests from outside the lobby are closed");
        api.Incoming.Enqueue((99, new byte[] { 1 }));
        api.Incoming.Enqueue((2, []));
        api.Incoming.Enqueue((2, new byte[8193]));
        api.Incoming.Enqueue((2, new byte[] { 2, 3 }));
        host.Poll();
        Check(
            host.Receive(out var message) && message.Source == "2" && message.Data.SequenceEqual(new byte[] { 2, 3 }),
            "Legacy wire validates membership and payload bounds before delivering packets"
        );
        Check(!host.Receive(out _), "Legacy invalid packets do not reach the lobby protocol");

        host.Send("2", new byte[1199], false);
        host.Send("2", new byte[1200], false);
        host.Send("2", new byte[8192], true);
        host.Send("99", new byte[1], true);
        bool oversizedRejected = false;
        try
        {
            host.Send("2", new byte[1201], false);
        }
        catch (ArgumentOutOfRangeException)
        {
            oversizedRejected = true;
        }
        Check(oversizedRejected, "Oversized gameplay datagrams cannot silently become reliable");
        Check(
            api.Sent.Select(packet => packet.Mode)
                .SequenceEqual(
                    new[] { EP2PSend.k_EP2PSendUnreliable, EP2PSend.k_EP2PSendUnreliable, EP2PSend.k_EP2PSendReliable }
                ),
            "Legacy wire keeps full-size 1200-byte gameplay packets unreliable and coordination reliable"
        );
        host.SetPeers(["2"]);
        host.SetPeers([]);
        Check(api.Closed.Contains(2) && !host.IsConnected("2"), "Removing a mesh peer closes its legacy session");

        var guestApi = new FakeApi(2);
        using var guest = new SteamLegacyWire(guestApi, false, 1);
        guest.Send("1", [10], true);
        Check(
            guestApi.Sent.Count == 1 && !guest.IsConnected("1"),
            "Legacy initial send starts the native session without waiting for a connection callback"
        );
        guestApi.Requests.Enqueue(3);
        guest.Poll();
        Check(guestApi.Closed.Contains(3), "Guests reject unapproved mesh links even for lobby members");
        guest.SetPeers(["1", "3"]);
        guestApi.Requests.Enqueue(3);
        guest.Poll();
        Check(guestApi.Accepted.Contains(3), "Guests accept host-approved mesh peers");
        guest.SetPeers([]);
        guest.Send("1", [11], false);
        Check(
            guestApi.Sent.Count == 2 && !guestApi.Closed.Contains(1),
            "Guests retain their host link when spectating"
        );
        guest.Dispose();
        guest.Dispose();
        Check(
            guestApi.DisposeCount == 1 && guestApi.Closed.Count(id => id == 1) == 1,
            "Legacy disposal closes each session and callback owner once"
        );
    }

    private static void BackpressureAndFailure()
    {
        var api = new FakeApi(2) { BlockSends = true };
        using var wire = new SteamLegacyWire(api, false, 1);
        byte[] payload = [1];
        wire.Send("1", payload, true);
        payload[0] = 99;
        wire.Send("1", [2], true);
        api.BlockSends = false;
        wire.Poll();
        Check(
            api.Sent.Select(p => p.Data[0]).SequenceEqual(new byte[] { 1, 2 }),
            "Reliable retries preserve owned payloads and ordering after backpressure"
        );
        api.Failure = (1, EP2PSessionError.k_EP2PSessionErrorTimeout);
        wire.Poll();
        Check(
            wire.Error != null && wire.TakeDisconnected(out var address) && address == "1",
            "Legacy native failures reach the lobby and release the failed connection"
        );

        var congested = new FakeApi(2) { BlockSends = true };
        using var bounded = new SteamLegacyWire(congested, false, 1);
        for (int n = 0; n < 513; n++)
            bounded.Send("1", [1], true);
        Check(bounded.Error == "Reliable send queue filled", "Legacy reliable backpressure has a bounded queue");

        var burst = new FakeApi(1);
        using var receiver = new SteamLegacyWire(burst, true, 1);
        for (int n = 0; n < 5000; n++)
            burst.Incoming.Enqueue((2, new byte[] { 1 }));
        receiver.Poll();
        Check(burst.Incoming.Count == 2952, "Legacy receive work is bounded per outer poll");
        receiver.Poll();
        receiver.Poll();
        int count = 0;
        while (receiver.Receive(out _))
            count++;
        Check(count == 4096 && receiver.Statistics.Ignored == 904, "Legacy receive queue cannot grow without limit");
    }

    private static void FullMeshLobby()
    {
        var apis = Enumerable.Range(1, 3).Select(id => new FakeApi((ulong)id)).ToArray();
        var wires = apis.Select(api => new SteamLegacyWire(api, api.LocalId == 1, 1)).ToArray();
        var lobbies = wires
            .Select(wire => new MeshLobby(
                wire,
                wire.LocalAddress == "1" ? null : "1",
                8,
                [new LobbyPlayer(0, Spawned: true)],
                "legacy-test",
                "{}"
            ))
            .ToArray();
        var simulations = lobbies
            .Select(lobby => new LobbySimulation(
                new World(TestFixtures.Map(), new GameRules { Lobby = true, PlayerCount = 8 }, 13),
                lobby.Roster,
                71
            ))
            .ToArray();
        var pending = new List<(long Due, ulong Source, ulong Target, byte[] Data)>();
        long now = 0;
        foreach (var api in apis)
            api.OnSend = packet =>
            {
                pending.Add((now + 16, api.LocalId, packet.Target, packet.Data));
                return true;
            };
        for (int n = 0; n < lobbies.Length; n++)
            lobbies[n].AttachSimulation(simulations[n]);
        try
        {
            void Step()
            {
                now += 9;
                foreach (var api in apis)
                    api.TimeMilliseconds = now;
                foreach (var packet in pending.Where(packet => packet.Due <= now).ToArray())
                {
                    pending.Remove(packet);
                    var destination = apis[(int)packet.Target - 1];
                    if (destination.DisposeCount > 0)
                        continue;
                    if (!destination.Accepted.Contains(packet.Source))
                        destination.Requests.Enqueue(packet.Source);
                    destination.Incoming.Enqueue((packet.Source, packet.Data));
                }
                foreach (var lobby in lobbies)
                    lobby.Poll();
                for (int n = 0; n < lobbies.Length; n++)
                    if (apis[n].DisposeCount == 0 && lobbies[n].LobbySession is { } session)
                        session.TryAdvance(
                            session
                                .LocalSlots.Select(handle =>
                                    simulations[n].InputSources[handle].HostCommand
                                        ? new RollbackInput(default, Cpu: lobbies[n].HostCommand)
                                        : default
                                )
                                .ToArray()
                        );
            }
            for (int frame = 0; frame < 1800; frame++)
                Step();
            Check(
                lobbies.All(lobby =>
                    lobby.Error == null
                    && lobby.SimulationReady
                    && lobby.Roster.Count == 3
                    && lobby.LobbySession is { State: GGCS.SessionState.Running, Error: null }
                ),
                "Three-machine legacy adapter completes admission and continues running rollback"
            );
            Check(
                simulations.All(simulation => simulation.World.TickNumber > 1500),
                "Legacy simulation continues well beyond the observed native ICE failure interval"
            );
            Check(
                apis[1].Sent.Any(packet => packet.Target == 3) && apis[2].Sent.Any(packet => packet.Target == 2),
                "Legacy active guests exchange traffic directly instead of routing through the host"
            );
            long tick = lobbies.Min(lobby => lobby.LobbySession!.ConfirmedFrame) + 1;
            byte[]? expected = null;
            foreach (var lobby in lobbies)
            {
                Check(
                    lobby.LobbySession!.TryGetConfirmedCheckpoint(tick, out var state),
                    "Legacy confirmed checkpoint retained"
                );
                Check(expected == null || expected.SequenceEqual(state), "Legacy full-mesh confirmed worlds agree");
                expected = state;
            }
            lobbies[2].Dispose();
            for (int frame = 0; frame < 120; frame++)
                Step();
            Check(
                lobbies[0].Roster.Count == 2 && lobbies[1].Roster.Count == 2,
                "Legacy graceful departure removes the player without waiting for inactivity expiry"
            );
        }
        finally
        {
            foreach (var lobby in lobbies)
                lobby.Dispose();
        }
    }

    private sealed record SentPacket(ulong Target, byte[] Data, EP2PSend Mode);

    private sealed class FakeApi(ulong id) : ISteamLegacyApi
    {
        public ulong LocalId => id;
        public long TimeMilliseconds { get; set; }
        public event Action<ulong>? SessionRequested;
        public event Action<ulong, EP2PSessionError>? SessionFailed;
        public readonly Queue<ulong> Requests = new();
        public readonly Queue<(ulong Source, byte[] Data)> Incoming = new();
        public readonly HashSet<ulong> Accepted = new();
        public readonly List<ulong> Closed = new();
        public readonly List<SentPacket> Sent = new();
        public (ulong Source, EP2PSessionError Error)? Failure;
        public Func<SentPacket, bool>? OnSend;
        public bool BlockSends;
        public int DisposeCount;

        public bool IsMember(ulong peer) => peer is >= 1 and <= 3;

        public void Poll()
        {
            while (Requests.TryDequeue(out ulong source))
                SessionRequested?.Invoke(source);
            if (Failure is { } failure)
            {
                Failure = null;
                SessionFailed?.Invoke(failure.Source, failure.Error);
            }
        }

        public bool Accept(ulong peer)
        {
            Accepted.Add(peer);
            return true;
        }

        public void Close(ulong peer)
        {
            Closed.Add(peer);
            Accepted.Remove(peer);
        }

        public bool Send(ulong peer, byte[] data, EP2PSend mode)
        {
            if (BlockSends)
                return false;
            var packet = new SentPacket(peer, data.ToArray(), mode);
            if (OnSend?.Invoke(packet) == false)
                return false;
            Accepted.Add(peer);
            Sent.Add(packet);
            return true;
        }

        public bool Receive(out ulong source, out byte[] data)
        {
            source = 0;
            data = [];
            if (!Incoming.TryDequeue(out var packet))
                return false;
            source = packet.Source;
            data = packet.Data;
            return true;
        }

        public void Dispose() => DisposeCount++;
    }
}
