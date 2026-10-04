using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class NetworkSessionTests
{
    public static void OneRoundTripHandshake()
    {
        var wire = new SimulatedNetwork(2, delayTicks: 60, jitterTicks: 0, lossPercent: 0, duplicatePercent: 0);
        int[][] slots =
        [
            [0],
            [1],
        ];
        var sessions = Enumerable
            .Range(0, 2)
            .Select(peer =>
            {
                var world = MakeWorld(2);
                return new NetworkSession(
                    world,
                    new SessionConfig(slots, peer, "handshake", world, 1),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        try
        {
            for (int tick = 0; tick < 125; tick++)
            {
                wire.Advance();
                foreach (var session in sessions)
                    session.Poll();
            }
            Check(
                sessions.All(session => session.State == GGCS.SessionState.Running && session.Error == null),
                "A checked game session must not repeat five sequential handshake round trips"
            );
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    public static void InputWaitIndicator(bool hostSpectates = false)
    {
        var network = new SimulatedNetwork(3, delayTicks: 1, jitterTicks: 0, lossPercent: 0, duplicatePercent: 0);
        int[][] slots = hostSpectates
            ?
            [
                [0],
                [1, 2],
                [3, 4],
            ]
            :
            [
                [0],
                [1, 2],
                [3],
            ];
        var sessions = Enumerable
            .Range(0, 3)
            .Select(peer =>
            {
                var world = MakeWorld(4);
                return new NetworkSession(
                    world,
                    new SessionConfig(
                        slots,
                        peer,
                        "wait-test",
                        world,
                        1,
                        rollback: FixedTiming,
                        inputPlayerSlots: hostSpectates ? [-1, 0, 1, 2, 3] : null
                    ),
                    network.Endpoint(peer)
                );
            })
            .ToArray();
        void Step()
        {
            network.Advance();
            foreach (var session in sessions)
            {
                session.Poll();
                session.TryAdvance(
                    session
                        .LocalSlots.Select(handle =>
                            session.PlayerSlot(handle) < 0
                                ? default
                                : Input(session.World.TickNumber, session.PlayerSlot(handle))
                        )
                        .ToArray()
                );
                Check(session.Error == null, session.Error ?? "Unexpected wait-test error");
            }
        }
        try
        {
            Check(
                sessions.All(session => session.WaitingForInputPlayers.Count == 0),
                "Handshake is not an input stall"
            );
            for (int tick = 0; tick < 150; tick++)
                Step();
            Check(
                sessions
                    .Where(session => !hostSpectates || session.LocalPeer != 0)
                    .All(session => session.WaitingForInputPlayers.Count == 0),
                "Ordinary prediction has no wait banner"
            );
            network.Blackout = true;
            for (int tick = 0; tick < 60; tick++)
                Step();
            var host = sessions[0];
            Check(
                host.WaitingForInputPlayers.SequenceEqual(hostSpectates ? new[] { 1, 2, 3, 4 } : new[] { 1, 2, 3 }),
                "Input stall identifies every remote player, including shared-machine guests"
            );
            long stopped = host.World.TickNumber;
            Step();
            Check(host.World.TickNumber == stopped, "The wait banner describes a stopped simulation");
            host.StopAtTick(stopped);
            Check(host.WaitingForInputPlayers.Count == 0, "A checkpoint pause hides a prior input wait");
            host.StopAtTick(null);
            network.Blackout = false;
            bool cleared = false;
            for (int tick = 0; tick < 150; tick++)
            {
                Step();
                cleared |= host.WaitingForInputPlayers.Count == 0;
            }
            Check(host.World.TickNumber > stopped && cleared, "Input recovery clears the wait banner");
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    public static void ImpairedNetwork(int peers, bool mixed)
    {
        int players = mixed ? 8 : peers;
        int[][] slots = Enumerable
            .Range(0, peers)
            .Select(p => Enumerable.Range(p * (players / peers), players / peers).ToArray())
            .ToArray();
        var network = new SimulatedNetwork(peers, 7392, 6, 5, 12, 7);
        var sessions = Enumerable
            .Range(0, peers)
            .Select(p =>
            {
                var w = MakeWorld(players);
                return new NetworkSession(
                    w,
                    new SessionConfig(slots, p, "test-content", w, 1, rollback: FixedTiming),
                    network.Endpoint(p)
                );
            })
            .ToArray();
        const int target = 1200;
        long stalls = 0;
        for (
            int wall = 0;
            wall < 10000 && sessions.Any(s => s.World.TickNumber < target || s.ConfirmedFrame < target - 1);
            wall++
        )
        {
            network.Blackout = wall is >= 300 and < 380; // Burst loss longer than prediction window must stall, then recover.
            network.Advance();
            foreach (var session in sessions)
            {
                session.Poll();
                Check(session.Error == null, session.Error ?? "Unexpected network error");
                if (
                    session.World.TickNumber < target
                    && !session.TryAdvance(
                        session.LocalSlots.Select(slot => Input(session.World.TickNumber, slot)).ToArray()
                    )
                )
                {
                    stalls++;
                }
            }
        }

        var baseline = MakeWorld(players);
        for (int tick = 0; tick < target; tick++)
        {
            baseline.Tick(Enumerable.Range(0, players).Select(p => Input(tick - 2, p)).ToArray());
        }

        foreach (var s in sessions)
        {
            Check(
                s.World.TickNumber == target && s.ConfirmedFrame == target - 1,
                "Session failed to converge after loss/stall"
            );
            Check(
                s.World.HashState() == baseline.HashState(),
                $"{peers} peers desynchronized from baseline: {s.World.HashState():x16} vs {baseline.HashState():x16}"
            );
            Check(s.RollbackCount > 0, "Stress test did not exercise rollback");
            Check(s.PredictionDepth == 0, "Final timeline remains predicted");
        }

        for (int attempt = 0; attempt < 100 && sessions.Any(s => !s.AllPeersConfirmed(target - 1)); attempt++)
        {
            network.Advance();
            foreach (var s in sessions)
            {
                s.Poll();
            }
        }

        foreach (var s in sessions)
        {
            Check(s.AllPeersConfirmed(target - 1), "Peers did not acknowledge and hash final state");
            s.Dispose();
        }

        Check(stalls > 0, "Burst-loss test did not stall at rollback limit");
        Check(network.MaximumPacketBytes + 15 <= 1200, "Rollback packets plus the mesh envelope fit one UDP fragment");
        Console.WriteLine(
            $"Rollback: {peers} peers/{players} players, {target} ticks, {sessions.Sum(s => s.RollbackCount)} rewinds, {stalls} stalls, {network.BytesSent / 1024} KiB"
        );
    }

    public static void ClockDrift()
    {
        var network = new SimulatedNetwork(2, 553, 7, 4, 5, 3);
        int[][] slots =
        [
            [0],
            [1],
        ];
        var sessions = Enumerable
            .Range(0, 2)
            .Select(p =>
            {
                var w = MakeWorld(2);
                return new NetworkSession(
                    w,
                    new SessionConfig(slots, p, "clock", w, 1, rollback: FixedTiming),
                    network.Endpoint(p)
                );
            })
            .ToArray();
        int slowdowns = 0;
        double[] accumulator = new double[2];
        int maxLead = 0;
        for (int wall = 0; wall < 5000 && sessions.Any(s => s.World.TickNumber < 900 || s.ConfirmedFrame < 899); wall++)
        {
            network.Advance();
            foreach (var s in sessions)
            {
                s.Poll();
            }

            for (int p = 0; p < 2; p++)
            {
                var s = sessions[p];
                Check(s.Error == null, s.Error ?? "Clock test error");
                accumulator[p] += p == 0 ? 1.025 : 0.975;
                while (accumulator[p] >= s.FrameDurationMultiplier && s.World.TickNumber < 900)
                {
                    double duration = s.FrameDurationMultiplier;
                    if (duration > 1)
                        slowdowns++;
                    if (!s.TryAdvance([Input(s.World.TickNumber, p)]))
                    {
                        accumulator[p] = Math.Min(accumulator[p], duration);
                        break;
                    }
                    accumulator[p] -= duration;
                }
            }

            maxLead = Math.Max(maxLead, (int)Math.Abs(sessions[0].World.TickNumber - sessions[1].World.TickNumber));
        }

        Check(
            sessions.All(s => s.World.TickNumber == 900 && s.ConfirmedFrame == 899),
            "Clock drift pacing failed to converge"
        );
        Check(sessions[0].World.HashState() == sessions[1].World.HashState(), "Clock drift changed simulation");
        Check(slowdowns > 0 && maxLead < 24, "Pacing did not bound clock lead");
        Console.WriteLine($"Clock drift: bounded lead {maxLead} ticks, {slowdowns} paced ticks");
        foreach (var s in sessions)
        {
            s.Dispose();
        }
    }

    public static void RenderRateTraffic()
    {
        long baseline = Run(1);
        long highRate = Run(16);
        Check(highRate <= baseline * 1.05, "Render polling multiplied network send rate");
        Console.WriteLine($"Render-rate isolation: 1 poll {baseline} packets; 16 polls {highRate} packets");

        static long Run(int polls)
        {
            const int peers = 8;
            var network = new SimulatedNetwork(peers, 45, 1, 0, 0, 0);
            var slots = Enumerable.Range(0, peers).Select(peer => new[] { peer }).ToArray();
            var sessions = Enumerable
                .Range(0, peers)
                .Select(peer =>
                {
                    var world = MakeWorld(peers);
                    return new NetworkSession(
                        world,
                        new SessionConfig(slots, peer, "fps", world, 1, rollback: FixedTiming),
                        network.Endpoint(peer)
                    );
                })
                .ToArray();
            Synchronize(network, sessions);
            long started = network.PacketsSent;
            for (int tick = 0; tick < 240; tick++)
            {
                network.Advance();
                for (int poll = 0; poll < polls; poll++)
                    foreach (var session in sessions)
                        session.Poll();
                foreach (var session in sessions)
                {
                    Check(
                        session.TryAdvance([Input(session.World.TickNumber, session.LocalPeer)]),
                        "High-render-rate session stalled"
                    );
                    Check(session.Error == null, session.Error ?? "High-FPS network error");
                }
            }
            long packets = network.PacketsSent - started;
            foreach (var session in sessions)
                session.Dispose();
            return packets;
        }
    }

    internal static void Synchronize(SimulatedNetwork network, NetworkSession[] sessions)
    {
        for (int wall = 0; wall < 3000 && sessions.Any(session => session.State != GGCS.SessionState.Running); wall++)
        {
            network.Advance();
            foreach (var session in sessions)
            {
                session.Poll();
                Check(session.Error == null, session.Error ?? "Session failed to synchronize");
            }
        }
        Check(sessions.All(session => session.State == GGCS.SessionState.Running), "Mesh handshake did not complete");
    }

    public static void CheckpointContinuation()
    {
        int[][] slots =
        [
            [0],
            [1],
        ];
        var wire = new SimulatedNetwork(2, 713, 5, 3, 6, 2);
        NetworkSession[] sessions = Enumerable
            .Range(0, 2)
            .Select(peer =>
            {
                var world = MakeWorld(2);
                return new NetworkSession(
                    world,
                    new SessionConfig(slots, peer, "checkpoint", world, 10, rollback: FixedTiming),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        const int boundary = 173;
        foreach (var session in sessions)
            session.StopAtTick(boundary);
        RunUntil(boundary);
        var checkpoints = new byte[2][];
        var initialInputs = new Dictionary<int, RollbackInput[]>();
        foreach (var session in sessions)
        {
            Check(
                session.TryGetConfirmedCheckpoint(boundary, out checkpoints[session.LocalPeer]),
                "Confirmed checkpoint is available"
            );
            initialInputs.Add(session.LocalSlots[0], session.ExportPendingLocalInputs(boundary)[0]);
            session.Dispose();
        }
        Check(checkpoints[0].SequenceEqual(checkpoints[1]), "Checkpoint states agree at the boundary");
        sessions = Enumerable
            .Range(0, 2)
            .Select(peer =>
            {
                var world = MakeWorld(2);
                world.Restore(checkpoints[peer]);
                return new NetworkSession(
                    world,
                    new SessionConfig(
                        slots,
                        peer,
                        "checkpoint",
                        world,
                        11,
                        initialInputs: initialInputs,
                        rollback: FixedTiming
                    ),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        RunUntil(550);
        var expected = MakeWorld(2);
        for (int tick = 0; tick < 550; tick++)
            expected.Tick([Input(tick - 2, 0), Input(tick - 2, 1)]);
        foreach (var session in sessions)
        {
            Check(
                session.World.HashState() == expected.HashState(),
                "Checkpoint restart preserved delayed inputs and world tick"
            );
            session.Dispose();
        }
        Console.WriteLine("Checkpoint continuation: pending delayed inputs survive a fresh generation");

        void RunUntil(int target)
        {
            for (int wall = 0; wall < 3000 && sessions.Any(session => !session.AllPeersConfirmed(target - 1)); wall++)
            {
                wire.Advance();
                foreach (var session in sessions)
                {
                    session.Poll();
                    Check(session.Error == null, session.Error ?? "Checkpoint session failed");
                    if (session.World.TickNumber < target)
                        session.TryAdvance([Input(session.World.TickNumber, session.LocalPeer)]);
                }
            }
            Check(
                sessions.All(session => session.World.TickNumber == target && session.AllPeersConfirmed(target - 1)),
                "Checkpoint boundary confirmed"
            );
        }
    }

    public static void InputLatching()
    {
        int[][] slots =
        [
            [0],
            [1],
        ];
        var wire = new SimulatedNetwork(2, 312, 1, 0, 0, 0);
        var sessions = Enumerable
            .Range(0, 2)
            .Select(peer =>
            {
                var world = MakeWorld(2);
                return new NetworkSession(
                    world,
                    new SessionConfig(slots, peer, "latching", world, 1, rollback: FixedTiming),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        Synchronize(wire, sessions);
        NetworkSession fast = sessions[0];
        for (int tick = 0; tick < 40; tick++)
        {
            wire.Advance();
            if (!fast.TryAdvance([Input(fast.World.TickNumber, 0)]))
                break;
        }
        Check(fast.LocalInputSubmitted, "First stalled attempt submits its input");
        long latchedTick = fast.World.TickNumber;
        Check(fast.LastSubmittedTick == latchedTick, "Submission reports the stalled tick");
        Check(
            !fast.TryAdvance([new FrogSmashers.Core.InputFrame(1, 1, FrogSmashers.Core.InputButtons.Attack)]),
            "Missing peer still limits prediction"
        );
        Check(
            !fast.LocalInputSubmitted && fast.LastSubmittedTick == latchedTick,
            "Retry does not consume a second set of button edges"
        );
        for (int wall = 0; wall < 1000 && sessions.Any(session => !session.AllPeersConfirmed(199)); wall++)
        {
            wire.Advance();
            foreach (var session in sessions)
            {
                session.Poll();
                if (session.World.TickNumber < 200)
                    session.TryAdvance([Input(session.World.TickNumber, session.LocalPeer)]);
                Check(session.Error == null, session.Error ?? "Latched input session failed");
            }
        }
        var expected = MakeWorld(2);
        for (int tick = 0; tick < 200; tick++)
            expected.Tick([Input(tick - 2, 0), Input(tick - 2, 1)]);
        foreach (var session in sessions)
        {
            Check(
                session.World.TickNumber == 200 && session.World.HashState() == expected.HashState(),
                "Waiting retains the first input without losing or rewriting it"
            );
            session.Dispose();
        }
        Console.WriteLine("Input latching: edges are consumed once even when a frame must wait");
    }

    public static void HighLatencyThroughput()
    {
        const int peers = 8;
        var wire = new SimulatedNetwork(peers, 8721, World.TicksFromSeconds(.15m), World.TicksFromSeconds(.025m), 2, 1);
        var slots = Enumerable.Range(0, peers).Select(peer => new[] { peer }).ToArray();
        var sessions = Enumerable
            .Range(0, peers)
            .Select(peer =>
            {
                var world = MakeWorld(peers);
                return new NetworkSession(
                    world,
                    new SessionConfig(slots, peer, "high-latency", world, 1, rollback: FixedTiming),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        Synchronize(wire, sessions);
        double[] accumulators = new double[peers];
        for (int tick = 0; tick < World.TickRate * 10; tick++)
        {
            wire.Advance();
            foreach (var session in sessions)
            {
                session.Poll();
                Check(session.Error == null, session.Error ?? "High latency failure");
                int peer = session.LocalPeer;
                accumulators[peer] += 1;
                while (accumulators[peer] >= session.FrameDurationMultiplier)
                {
                    double duration = session.FrameDurationMultiplier;
                    if (!session.TryAdvance([Input(session.World.TickNumber, peer)]))
                    {
                        accumulators[peer] = Math.Min(accumulators[peer], duration);
                        break;
                    }
                    accumulators[peer] -= duration;
                }
            }
        }
        double minimumRate = sessions.Min(session => session.World.TickNumber) / 10.0;
        Check(minimumRate >= World.TickRate * .967, $"300 ms RTT reduced normal game speed to {minimumRate:F1} Hz");
        foreach (var session in sessions)
            session.Dispose();
        Console.WriteLine($"Game adapter: 8 machines, 300 ms RTT, jitter/loss, slowest {minimumRate:F1} Hz");
    }

    public static void TerminalBarrier()
    {
        const int peers = 4;
        const int target = 360;
        var network = new SimulatedNetwork(peers, 6651, 9, 8, 35, 12);
        var slots = Enumerable.Range(0, peers).Select(p => new[] { p }).ToArray();
        var sessions = Enumerable
            .Range(0, peers)
            .Select(p =>
            {
                var w = MakeWorld(peers);
                return new NetworkSession(
                    w,
                    new SessionConfig(slots, p, "terminal", w, 1, rollback: FixedTiming),
                    network.Endpoint(p)
                );
            })
            .ToArray();
        int firstFinished = -1;
        for (int wall = 0; wall < 5000 && firstFinished < 0; wall++)
        {
            network.Advance();
            for (int p = 0; p < peers; p++)
            {
                var s = sessions[p];
                s.Poll();
                Check(s.Error == null, s.Error ?? "Terminal barrier error");
                if (s.World.TickNumber < target)
                {
                    s.TryAdvance([Input(s.World.TickNumber, p)]);
                }

                if (s.AllPeersConfirmed(target - 1))
                {
                    firstFinished = p;
                    break;
                }
            }
        }

        Check(firstFinished >= 0, "Terminal barrier never completed under packet loss");
        ulong hash = sessions[firstFinished].World.HashState();
        foreach (var s in sessions)
        {
            Check(
                s.World.TickNumber == target && s.ConfirmedFrame == target - 1 && s.World.HashState() == hash,
                "First departing peer left another without final inputs/state"
            );
        }

        for (int wall = 0; wall < 60; wall++)
        {
            network.Advance();
            for (int p = 0; p < peers; p++)
            {
                if (p != firstFinished)
                {
                    sessions[p].Poll();
                }
            }
        }

        foreach (var s in sessions)
        {
            Check(s.World.HashState() == hash, "Final state changed after peer departure");
            s.Dispose();
        }

        Console.WriteLine(
            "Terminal barrier: first departure leaves every peer with confirmed matching final state (35% loss)"
        );
    }
}
