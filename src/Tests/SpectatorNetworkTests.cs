using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class SpectatorNetworkTests
{
    public static void Run()
    {
        PlaybackBufferBoundaries();
        SmoothPlayback(60, 60, 0);
        SmoothPlayback(60, 60, 2);
        SmoothPlayback(30, 120, 0);
        SmoothPlayback(30, 120, 2);
        UnlinkedSpectatorsDoNotRemovePlayingPeers();
        Verify(false, false);
        Verify(true, false);
        Verify(false, true);
        Verify(false, false, 8);
    }

    private static void PlaybackBufferBoundaries()
    {
        var playback = new SpectatorPlayback();
        Check(!playback.Ready(0, 0, 0, false), "An empty spectator waits for inputs");
        Check(!playback.Ready(0, 2, 10, false), "Playback gathers a small reserve before starting");
        Check(playback.Ready(0, 6, 30, false), "Six queued ticks start spectator playback");
        Check(playback.Ready(5, 1, 40, false), "Running playback can consume its reserve through the final tick");
        Check(!playback.Ready(6, 0, 50, false), "An underrun starts buffering again");
        Check(!playback.Ready(6, 2, 60, false), "An underrun does not restart on each tiny packet");
        Check(!playback.Ready(6, 2, 159, false), "Polling alone does not reset the input arrival timer");
        Check(playback.Ready(6, 2, 160, false), "A short final stream drains after its bounded wait");
        Check(!playback.Ready(8, 0, 170, false), "Drained playback waits for new inputs");
        Check(playback.Ready(8, 1, 180, true), "A coordinated pause drains its remaining input immediately");
        Check(playback.FrameDurationMultiplier(3) == 1, "A normal packet burst cannot trigger double speed");
        Check(playback.FrameDurationMultiplier(20) < 1, "A large spectator backlog can still catch up");
    }

    private static void SmoothPlayback(int hostFps, int spectatorFps, int jitter)
    {
        int[][] slots =
        [
            [0, 1],
            [],
        ];
        const int clockRate = 600;
        var wire = new SimulatedNetwork(2, 83, clockRate / 10, jitter * 5, 0, 0) { ClockRate = clockRate };
        var hostWorld = MakeWorld(2);
        var spectatorWorld = MakeWorld(2);
        using var host = new NetworkSession(
            hostWorld,
            new SessionConfig(slots, 0, "spectator-playback", hostWorld, 1),
            wire.Endpoint(0)
        );
        using var spectator = new NetworkSession(
            spectatorWorld,
            new SessionConfig(slots, 1, "spectator-playback", spectatorWorld, 1),
            wire.Endpoint(1)
        );
        int hostStride = clockRate / hostFps;
        int spectatorStride = clockRate / spectatorFps;
        double tickStep = (double)World.TickRate / clockRate;
        double hostDebt = 0,
            spectatorDebt = 0;
        int warnings = 0,
            emptyUpdates = 0;
        long start = 0;
        for (int wall = 0; wall < clockRate * 25; wall++)
        {
            wire.Advance();
            if (wall % hostStride == 0)
                Advance(host, ref hostDebt, hostStride * tickStep);
            if (wall % spectatorStride != 0)
                continue;
            long previous = spectatorWorld.TickNumber;
            Advance(spectator, ref spectatorDebt, spectatorStride * tickStep);
            if (wall == clockRate * 5)
                start = spectatorWorld.TickNumber;
            if (wall >= clockRate * 5)
            {
                warnings += spectator.WaitingForHostInputs ? 1 : 0;
                emptyUpdates += spectatorWorld.TickNumber == previous ? 1 : 0;
            }
        }
        string scenario =
            $"200 ms RTT, host {hostFps} FPS, spectator {spectatorFps} FPS, jitter {jitter * 1000.0 / 120:F1} ms";
        Check(warnings == 0, $"Healthy spectator stream must not show input waits: {scenario}, warnings={warnings}");
        int expectedEmptyUpdates = Math.Max(0, spectatorFps - World.TickRate) * 20;
        Check(
            emptyUpdates <= expectedEmptyUpdates + 2,
            $"Buffered playback must sustain its tick rate: {scenario}, empty={emptyUpdates}"
        );
        Check(
            spectatorWorld.TickNumber - start >= World.TickRate * 20 - 10,
            "Buffered spectators sustain the simulation tick rate"
        );
        Check(
            hostWorld.TickNumber - spectatorWorld.TickNumber < World.TickRate / 3,
            "Spectator playback delay stays bounded"
        );
        Check(
            host.TryGetConfirmedCheckpoint(spectatorWorld.TickNumber, out var snapshot)
                && snapshot.SequenceEqual(spectatorWorld.Capture()),
            "Spectator buffering preserves the exact confirmed simulation"
        );

        wire.Blackout = true;
        bool waited = false;
        for (int wall = 0; wall < clockRate; wall++)
        {
            wire.Advance();
            Advance(host, ref hostDebt, tickStep);
            Advance(spectator, ref spectatorDebt, tickStep);
            waited |= spectator.WaitingForHostInputs;
        }
        Check(waited, "A genuine spectator outage still displays the input wait warning");
        wire.Blackout = false;
        for (int wall = 0; wall < clockRate * 3; wall++)
        {
            wire.Advance();
            Advance(host, ref hostDebt, tickStep);
            Advance(spectator, ref spectatorDebt, tickStep);
        }
        Check(!spectator.WaitingForHostInputs, "Spectator playback recovers after an outage");

        long finalTick = hostWorld.TickNumber;
        spectator.StopAtTick(finalTick);
        for (int wall = 0; wall < clockRate * 3 && spectatorWorld.TickNumber < finalTick; wall++)
        {
            wire.Advance();
            host.Poll();
            Advance(spectator, ref spectatorDebt, tickStep);
        }
        Check(spectatorWorld.TickNumber == finalTick, "Spectator buffering cannot block a checkpoint boundary");
        Check(
            spectatorWorld.HashState() == hostWorld.HashState(),
            "The drained spectator reaches the final host state"
        );
        Check(!spectator.WaitingForHostInputs, "A completed spectator checkpoint is not an input stall");
        Console.WriteLine($"Spectator playback: {scenario}; no steady-stream warnings or missed updates");
    }

    private static void Advance(NetworkSession session, ref double debt, double elapsedTicks)
    {
        session.Poll();
        debt += elapsedTicks;
        for (int step = 0; step < 30; step++)
        {
            double duration = session.FrameDurationMultiplier;
            if (debt + 1e-9 < duration)
                break;
            if (!session.TryAdvance(new RollbackInput[session.LocalSlots.Length]))
            {
                debt = Math.Min(debt, duration);
                break;
            }
            debt -= duration;
        }
        Check(session.Error == null, session.Error ?? "Spectator playback network failure");
    }

    private static void UnlinkedSpectatorsDoNotRemovePlayingPeers()
    {
        int[][] slots =
        [
            [0],
            [1],
            [],
        ];
        var wire = new SimulatedNetwork(3, 72, 1, 0, 0, 0);
        var world = MakeWorld(2);
        using var session = new NetworkSession(
            world,
            new SessionConfig(slots, 1, "removal", world, 1, rollback: FixedTiming),
            wire.Endpoint(1)
        );
        session.DisconnectPeer(2);
        session.DisconnectPeer(3);
        session.DisconnectPeer(1);
        Check(
            session.Error == null && session.PeerStats.Count == 1,
            "Spectators, pending admissions and the local peer have no removable active mesh link"
        );
    }

    private static void Verify(bool hostSpectates, bool absentObserver, int playerCount = 2)
    {
        int[][] slots = hostSpectates
            ?
            [
                [0],
                [1],
                [2],
                [],
            ]
            :
            [
                [0],
                [1],
                [],
                [],
            ];
        if (playerCount == 8)
            slots = Enumerable.Range(0, 12).Select(peer => peer < 8 ? new[] { peer } : Array.Empty<int>()).ToArray();
        var wire = new SimulatedNetwork(slots.Length, 991, 3, 2, 8, 3);
        var sessions = Enumerable
            .Range(0, slots.Length)
            .Select(peer =>
            {
                var world = MakeWorld(playerCount);
                return new NetworkSession(
                    world,
                    new SessionConfig(
                        slots,
                        peer,
                        "spectators",
                        world,
                        1,
                        rollback: FixedTiming,
                        inputPlayerSlots: hostSpectates ? [-1, 0, 1] : null
                    ),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        const int target = 750;
        int running = absentObserver ? slots.Length - 1 : slots.Length;
        for (int wall = 0; wall < 6000 && sessions.Take(running).Any(s => !s.AllPeersConfirmed(target - 1)); wall++)
        {
            wire.Advance();
            foreach (var session in sessions.Take(running))
            {
                session.Poll();
                Check(session.Error == null, session.Error ?? "Spectator network failure");
                if (session.World.TickNumber < target)
                    session.TryAdvance(
                        session
                            .LocalSlots.Select(handle =>
                                session.PlayerSlot(handle) < 0
                                    ? default
                                    : Input(session.World.TickNumber, session.PlayerSlot(handle))
                            )
                            .ToArray()
                    );
            }
        }
        var baseline = MakeWorld(playerCount);
        for (int tick = 0; tick < target; tick++)
            baseline.Advance(
                Enumerable
                    .Range(0, playerCount)
                    .Select(slot => Input(Math.Max(0, tick - 2), slot))
                    .Select(input => tick < 2 ? default : input)
                    .Select(frame => new MatchInput(frame))
                    .ToArray()
            );
        foreach (var session in sessions.Take(running))
        {
            Check(
                session.World.TickNumber == target && session.AllPeersConfirmed(target - 1),
                $"Players and spectators reach the final barrier without waiting for absent observers: hostSpectates={hostSpectates}, absent={absentObserver}, peer={session.LocalPeer}, tick={session.World.TickNumber}, confirmed={session.ConfirmedFrame}, wait={session.WaitReason}"
            );
            Check(
                session.World.HashState() == baseline.HashState(),
                "Spectator replay diverged from the player simulation"
            );
        }
        foreach (var session in sessions)
            session.Dispose();
        Console.WriteLine($"Spectators: host={hostSpectates}, absent={absentObserver}, all active simulations agree");
    }
}
