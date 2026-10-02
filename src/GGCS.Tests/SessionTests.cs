namespace GGCS.Tests;

internal static class SessionTests
{
    public static void Run()
    {
        LocalAndDeterminism();
        foreach (int peers in new[] { 2, 4, 8 })
            ImpairedMesh(peers);
        HighLatency();
        DelayChangesAndMultipleLocalPlayers();
        PredictionStallAndRecovery();
        ClockDriftAndRenderRates();
        SpectatorsAndPassiveHost();
        SlowSpectator();
        LateSpectator();
        DisconnectAgreement();
        DesyncAndOldGeneration();
        ExplicitConfirmationAtPausedBoundary();
        Lockstep();
    }

    private static void ExplicitConfirmationAtPausedBoundary()
    {
        var clock = new TestClock();
        var wire = new SimulatedNetwork(clock) { Latency = 10, Loss = .2 };
        Player[] players = [new(0, 0), new(1, 1)];
        var options = new SessionOptions { ChecksumInterval = 0 };
        var sessions = Enumerable
            .Range(0, 2)
            .Select(peer => new P2PSession<int, ulong>(
                41,
                peer,
                players,
                new TestGame(),
                new IntCodec(),
                wire.Endpoint(peer),
                options,
                clock
            ))
            .ToArray();
        for (int tick = 0; tick < 2000 && sessions.Any(session => session.ConfirmedFrame < 82); tick++)
        {
            clock.NowMilliseconds += 8;
            foreach (var session in sessions)
            {
                session.Poll();
                if (session.CurrentFrame < 83)
                    session.AdvanceFrame([session.CurrentFrame]);
            }
        }
        Check.True(
            sessions.All(session => session.CurrentFrame == 83 && session.ConfirmedFrame == 82),
            "Both peers pause at the same confirmed state"
        );
        Check.True(!sessions[0].ConfirmState(83), "Local input confirmation is not a peer state hash acknowledgement");
        for (int tick = 0; tick < 1000 && !sessions[0].ConfirmState(83); tick++)
        {
            clock.NowMilliseconds += 8;
            foreach (var session in sessions)
                session.Poll();
        }
        Check.True(
            sessions[0].ConfirmState(83),
            "Peer answers an explicit checksum request without advancing or periodic hashes"
        );
        Check.True(sessions[1].ConfirmState(83), "Both terminal hashes are verified");
    }

    private static void LocalAndDeterminism()
    {
        var rig = new SessionRig([0, 0, 0]);
        rig.Run(1000);
        Check.True(rig.MinimumFrame >= 116, "local session runs without network peers");
        var syncGame = new TestGame();
        var sync = new SyncTestSession<int, ulong>(2, syncGame);
        for (int frame = 0; frame < 100; frame++)
            sync.AdvanceFrame([SessionRig.Input(frame, 0), SessionRig.Input(frame, 1)]);
        Check.True(sync.VerifiedFrames > 700, "determinism test resimulates rolling history");
        var broken = new SyncTestSession<int, ulong>(1, new TestGame { BrokenRestore = true });
        Check.Throws<DeterminismException>(
            () => broken.AdvanceFrame([1]),
            "determinism test detects incomplete restore"
        );
    }

    private static void ImpairedMesh(int peers)
    {
        var rig = new SessionRig(Enumerable.Range(0, peers).ToArray());
        rig.Network.Latency = 50;
        rig.Network.Jitter = 33;
        rig.Network.Loss = .05;
        rig.Network.Duplication = .02;
        rig.Run(4000);
        int before = rig.MinimumFrame;
        rig.Run(10000);
        double rate = (rig.MinimumFrame - before) / 10d;
        Console.WriteLine($"mesh {peers}: {rate:F1} Hz, max rollback {rig.Nodes.Max(n => n.Session.LargestRollback)}");
        Check.True(rate >= 117, $"{peers}-peer jitter/loss retains normal speed ({rate:F1} Hz)");
        Check.True(
            rig.Nodes.All(n => n.Session.TotalResimulatedFrames > 0),
            "impaired sessions actually repair predictions"
        );
        rig.CheckAgreement($"{peers}-peer jitter/loss");
        Check.Equal(peers * (peers - 1), rig.Network.Links.Count, "active players use all-to-all links");
    }

    private static void HighLatency()
    {
        foreach (int rtt in new[] { 100, 250, 300 })
        {
            var rig = new SessionRig([0, 1]);
            rig.Network.Latency = rtt / 2;
            rig.Run(4000);
            int before = rig.MinimumFrame;
            rig.Run(4000);
            double rate = (rig.MinimumFrame - before) / 4d;
            Console.WriteLine($"RTT {rtt}: {rate:F1} Hz");
            Check.True(rate >= 117, $"{rtt}ms RTT stays within one-way prediction budget: {rate:F1}");
            rig.CheckAgreement($"RTT {rtt}");
        }
    }

    private static void DelayChangesAndMultipleLocalPlayers()
    {
        var rig = new SessionRig([0, 0, 1, 1]);
        rig.Network.Latency = 40;
        rig.Nodes[0].Session.SetTiming(new() { DelayFrames = 5, MaxExtraDelayFrames = 0 });
        rig.Nodes[1].Session.SetTiming(new() { DelayFrames = 7, MaxExtraDelayFrames = 0 });
        rig.Run(2000);
        rig.Nodes[0].Session.SetTiming(new() { DelayFrames = 1, MaxExtraDelayFrames = 0 });
        rig.Nodes[1].Session.SetTiming(new() { DelayFrames = 6, MaxExtraDelayFrames = 0 });
        rig.Run(2000);
        rig.Nodes[1].Session.SetTiming(new() { DelayFrames = 0, MaxExtraDelayFrames = 0 });
        rig.Run(2000);
        rig.CheckAgreement("multiple local players and dynamic delays");
        Check.True(rig.MinimumFrame > 600, "different delay queues never leave network gaps");
    }

    private static void PredictionStallAndRecovery()
    {
        var rig = new SessionRig([0, 1, 2]);
        rig.Network.Latency = 30;
        rig.Run(2000);
        long begin = rig.Clock.NowMilliseconds;
        rig.Network.Block = (from, to, time) => (from == 2 || to == 2) && time < begin + 600;
        int before = rig.MinimumFrame;
        rig.Run(500);
        Check.True(rig.MinimumFrame - before <= 32, "prediction exhaustion pauses the mesh during an outage");
        rig.Run(1500);
        int recovered = rig.MinimumFrame;
        rig.Run(2000);
        Check.True(rig.MinimumFrame - recovered >= 232, "mesh returns to normal speed after outage");
        rig.CheckAgreement("outage recovery");
    }

    private static void ClockDriftAndRenderRates()
    {
        var rig = new SessionRig([0, 1, 2, 3]);
        rig.Network.Latency = 40;
        rig.Nodes[0].RenderHz = 30;
        rig.Nodes[1].RenderHz = 60;
        rig.Nodes[2].RenderHz = 144;
        rig.Nodes[3].RenderHz = 240;
        rig.Nodes[3].ClockRate = 1.04;
        rig.Run(15000);
        int spread = rig.Nodes.Max(n => n.Session.CurrentFrame) - rig.MinimumFrame;
        Check.True(spread < 18, $"smoothed pacing bounds clock drift: {spread} frames");
        rig.CheckAgreement("render rates and clock drift");
    }

    private static void SpectatorsAndPassiveHost()
    {
        var rig = new SessionRig([1, 1, 2, 3]);
        foreach (var node in rig.Nodes)
            node.Session.AddInputObserver(0);
        rig.AddNode(0);
        for (int spectator = 100; spectator < 104; spectator++)
            rig.AddSpectator(spectator, 0);
        rig.Network.Latency = 30;
        rig.Network.Jitter = 10;
        rig.Network.Loss = .03;
        rig.Run(7000);
        rig.CheckAgreement("spectating host receives all raw player streams");
        foreach (var spectator in rig.Spectators)
        {
            Check.True(spectator.Session.CurrentFrame > 600, "spectators catch up through host");
            Check.Equal(
                rig.Nodes[0].Game.States[spectator.Session.CurrentFrame],
                spectator.Game.Value,
                "spectator uses confirmed combined inputs"
            );
            Check.True(rig.Network.Links.Contains((0, spectator.Id)), "host supplies spectator inputs");
            Check.True(
                !rig.Network.Links.Any(link => link.To == spectator.Id && link.From != 0),
                "spectator receives only host stream"
            );
        }
    }

    private static void SlowSpectator()
    {
        var rig = new SessionRig(
            [0, 1],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 24,
                HistoryFrames = 60,
            }
        );
        rig.AddSpectator(100, 0);
        rig.Run(2000, advanceSpectators: false);
        Check.Equal(
            SessionState.Disconnected,
            rig.Spectators[0].Session.State,
            "slow spectator needs a new checkpoint after bounded buffer fills"
        );
        Check.True(rig.MinimumFrame > 220, "slow spectator never blocks active players");
        rig.CheckAgreement("slow spectator");
    }

    private static void LateSpectator()
    {
        var rig = new SessionRig([0, 1]);
        rig.Network.Latency = 20;
        rig.Run(2000);
        int start = rig.Nodes[0].Session.ConfirmedFrame;
        rig.AddSpectator(100, 0, start, rig.Nodes[0].Game.States[start]);
        rig.Run(2500);
        var spectator = rig.Spectators[0];
        Check.True(spectator.Session.CurrentFrame > start + 200, "late spectator replays from nonzero checkpoint");
        Check.Equal(
            rig.Nodes[0].Game.States[spectator.Session.CurrentFrame],
            spectator.Game.Value,
            "late checkpoint matches host state"
        );
    }

    private static void DisconnectAgreement()
    {
        var rig = new SessionRig([0, 1, 2]);
        rig.Network.Latency = 25;
        rig.Run(2500);
        long begin = rig.Clock.NowMilliseconds;
        rig.Network.Block = (from, to, time) => from == 2 && to == 0 && time >= begin;
        rig.Run(100);
        rig.Nodes[0].Session.DisconnectPeer(2);
        rig.Run(1000);
        Check.Equal(
            SessionState.Disconnected,
            rig.Nodes[2].Session.State,
            "excluded peer stops its session generation"
        );
        int frame = Math.Min(rig.Nodes[0].Session.ConfirmedFrame, rig.Nodes[1].Session.ConfirmedFrame) + 1;
        Check.True(frame > 300, "surviving players keep advancing");
        Check.Equal(
            rig.Nodes[0].Game.States[frame],
            rig.Nodes[1].Game.States[frame],
            "three-peer asymmetric disconnect chooses same cutoff"
        );
        Check.True(
            rig.Nodes.Take(2).All(n => !n.Events.Any(e => e.Kind == SessionEventKind.ProtocolError)),
            "survivors do not invalidate committed frames"
        );
    }

    private static void DesyncAndOldGeneration()
    {
        var rig = new SessionRig([0, 1]);
        rig.Nodes[1].Game.Bias = 1;
        rig.Run(1500);
        Check.True(
            rig.Nodes.Any(n => n.Events.Any(e => e.Kind == SessionEventKind.DesyncDetected)),
            "confirmed state checksums detect divergence"
        );
        var old = new SessionRig([0, 1], sessionId: 10);
        old.Network.CapturePackets = true;
        old.Run(100);
        var fresh = new SessionRig([0, 1], sessionId: 11);
        foreach (var packet in old.Network.Capture)
            fresh.Network.Inject(packet.From, packet.To, packet.Data);
        fresh.Run(500);
        fresh.CheckAgreement("old generation packets rejected");
        Check.True(fresh.Nodes[0].Session.GetNetworkStats(1).StalePackets > 0, "stale epoch counted in diagnostics");
    }

    private static void Lockstep()
    {
        var rig = new SessionRig(
            [0, 1],
            new()
            {
                FramesPerSecond = 60,
                MaxPredictionFrames = 0,
                InputDelay = 2,
            }
        );
        rig.Network.Latency = 30;
        rig.Network.Loss = .05;
        rig.Run(4000);
        Check.True(rig.MinimumFrame > 80, "zero prediction makes progress by sending inputs before waiting");
        Check.True(
            rig.Nodes.All(n => n.Session.TotalResimulatedFrames == 0),
            "lockstep does not predict and resimulate"
        );
        rig.CheckAgreement("lockstep");
    }
}
