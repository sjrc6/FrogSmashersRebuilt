namespace GGCS.Tests;

internal static class TimingTests
{
    private static SessionRig Create(int[]? owners = null)
    {
        var rig = new SessionRig(
            owners ?? [0, 1],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 30,
                HistoryFrames = 360,
                InputDelay = 2,
                MaxInputDelay = 240,
                SynchronizationRoundTrips = 1,
            }
        );
        foreach (var node in rig.Nodes)
            node.Session.SetTiming(new() { MaxExtraDelayFrames = 30 });
        return rig;
    }

    public static void Run()
    {
        PersonalDelay();
        Donation();
        AdaptiveRecovery();
        AdaptiveTransientRecovery();
        AdaptiveInputContinuity();
        PauseAtLimit();
        MultiplePeers();
        FrozenTiming();
        UnstableConnection();
        PerPlayerPrediction();
    }

    private static void PerPlayerPrediction()
    {
        var rig = Create([0, 1, 1, 2]);
        rig.Network.LinkLatency = (from, to) => from == 2 || to == 2 ? 100 : 10;
        rig.Run(8000);
        var session = rig.Nodes[0].Session;
        Check.Equal(0, session.PredictionForPlayer(0), "Local input is known without prediction");
        Check.Equal(
            session.PredictionForPlayer(1),
            session.PredictionForPlayer(2),
            "Two guests on one machine share an input horizon"
        );
        Check.True(
            session.PredictionForPlayer(3) > session.PredictionForPlayer(1),
            "A slower peer has its own larger prediction count"
        );
        Check.Equal(
            session.PredictionForPlayer(3),
            session.PredictionDepth,
            "Global prediction is the deepest player prediction"
        );
        Check.Throws<ArgumentOutOfRangeException>(() => session.PredictionForPlayer(-1), "Reject negative handles");
        Check.Throws<ArgumentOutOfRangeException>(() => session.PredictionForPlayer(4), "Reject missing handles");
        rig.CheckAgreement("per-player prediction diagnostics");
    }

    private static void FrozenTiming()
    {
        var rig = Create();
        var session = rig.Nodes[0].Session;
        session.FreezeTiming(true);
        session.SetTiming(
            new()
            {
                DelayFrames = 7,
                DonationFrames = 3,
                MaxExtraDelayFrames = 30,
            }
        );
        Check.Equal(2, session.EffectiveDelayFrames, "Timing remains stable during a checkpoint barrier");
        session.FreezeTiming(false);
        Check.Equal(7, session.EffectiveDelayFrames, "Deferred personal timing applies after checkpoint cancellation");
        session.RestoreExtraDelay(12);
        Check.Equal(19, session.EffectiveDelayFrames, "A checkpoint restores temporary response delay");
        rig.Run(1000);
        Check.Throws<InvalidOperationException>(
            () => session.RestoreExtraDelay(1),
            "Cannot restore timing into a running session"
        );
        rig.CheckAgreement("timing changes around a pause barrier");
    }

    private static void UnstableConnection()
    {
        var rig = Create([0, 1, 2, 3, 4, 5, 6, 7]);
        rig.Network.Latency = 280;
        rig.Network.Jitter = 65;
        rig.Network.Loss = .04;
        rig.Network.Duplication = .01;
        rig.Run(12000);
        int before = rig.MinimumFrame;
        rig.Run(6000);
        Console.WriteLine(
            $"Adaptive mesh: {(rig.MinimumFrame - before) / 6d:F1} Hz; "
                + string.Join(
                    "; ",
                    rig.Nodes.Select(n =>
                        $"{n.Id}: frame {n.Session.CurrentFrame}, extra {n.Session.ExtraDelayFrames}, prediction {n.Session.PredictionDepth}, ahead {n.Session.FramesAhead}"
                    )
                )
        );
        rig.CheckAgreement("adaptive mesh before outage");
        Check.True(rig.MinimumFrame - before >= 690, "Adaptive delay sustains play in an eight-peer jitter/loss mesh");
        long outage = rig.Clock.NowMilliseconds;
        rig.Network.Block = (from, to, time) => (from == 7 || to == 7) && time < outage + 700;
        rig.Run(3500);
        before = rig.MinimumFrame;
        rig.Run(3000);
        Check.True(rig.MinimumFrame - before >= 345, "The mesh recovers promptly after a temporary outage");
        Check.True(
            rig.Nodes.All(n => n.Session.ExtraDelayFrames <= n.Session.Timing.MaxExtraDelayFrames),
            "Loss and jitter cannot exceed personal delay limits"
        );
        rig.CheckAgreement("adaptive eight-peer jitter and outage recovery");
    }

    private static void PersonalDelay()
    {
        var rig = Create();
        rig.Network.Latency = 100;
        rig.Nodes[0].Session.SetTiming(new() { DelayFrames = 0, MaxExtraDelayFrames = 0 });
        rig.Nodes[1].Session.SetTiming(new() { DelayFrames = 12, MaxExtraDelayFrames = 0 });
        rig.Run(15000);
        int offset = rig.Nodes[0].Session.CurrentFrame - rig.Nodes[1].Session.CurrentFrame;
        Console.WriteLine(
            $"Personal delay: offset {offset}, prediction {rig.Nodes[0].Session.PredictionDepth}/{rig.Nodes[1].Session.PredictionDepth}"
        );
        Check.True(Math.Abs(offset - 12) <= 3, "Response delay shifts only the delayed machine's simulation behind");
        Check.True(
            rig.Nodes[1].Session.PredictionDepth <= rig.Nodes[0].Session.PredictionDepth - 6,
            "Personal response delay reduces local prediction of remote inputs"
        );
        Check.Equal(
            12,
            rig.Nodes[0].Session.GetNetworkStats(1).ResponseDelayFrames,
            "Remote response preference is visible"
        );
        rig.CheckAgreement("unequal personal delays");
    }

    private static void Donation()
    {
        var rig = Create();
        rig.Network.Latency = 100;
        rig.Nodes[0].Session.SetTiming(new() { DonationFrames = 10, MaxExtraDelayFrames = 0 });
        rig.Nodes[1].Session.SetTiming(new() { MaxExtraDelayFrames = 0 });
        rig.Run(15000);
        int lead = rig.Nodes[0].Session.CurrentFrame - rig.Nodes[1].Session.CurrentFrame;
        Console.WriteLine(
            $"Donation: lead {lead}, prediction {rig.Nodes[0].Session.PredictionDepth}/{rig.Nodes[1].Session.PredictionDepth}"
        );
        Check.True(Math.Abs(lead - 10) <= 3, "Donation grants the requested voluntary simulation lead");
        Check.True(
            rig.Nodes[0].Session.PredictionDepth > rig.Nodes[1].Session.PredictionDepth + 10,
            "Donating prediction burden benefits the other player"
        );
        Check.Equal(10, rig.Nodes[1].Session.GetNetworkStats(0).DonationFrames, "Remote donation is visible");
        rig.CheckAgreement("simulation lead donation");
    }

    private static void AdaptiveRecovery()
    {
        var rig = Create();
        rig.Network.Latency = 500;
        rig.Run(15000);
        int before = rig.MinimumFrame;
        rig.Run(10000);
        double rate = (rig.MinimumFrame - before) / 10d;
        Console.WriteLine(
            $"Adaptive 1000ms RTT: {rate:F1} Hz, extra {string.Join('/', rig.Nodes.Select(n => n.Session.ExtraDelayFrames))}"
        );
        Check.True(rate >= 116, "Adaptive response delay restores near-normal speed at sustained 1000ms RTT");
        Check.True(
            rig.Nodes.All(n => n.Session.ExtraDelayFrames is > 0 and <= 30),
            "Extra delay stays within the personal limit"
        );
        rig.CheckAgreement("adaptive high latency");
        rig.Network.Latency = 20;
        rig.Network.Jitter = 10;
        rig.Run(2500);
        Check.True(
            rig.Nodes.All(n => n.Session.ExtraDelayFrames == 0),
            "Recovered connection sheds 30 temporary delay ticks within 2.5 seconds"
        );
        rig.CheckAgreement("adaptive delay recovery");
    }

    private static void AdaptiveTransientRecovery()
    {
        foreach (string scenario in new[] { "startup", "one pause", "both pause", "background", "latency step" })
        {
            var rig = Create();
            rig.Network.Latency = 200;
            if (scenario == "startup")
            {
                rig.PauseNode = (id, time) => id == 1 && time < 600;
                rig.Run(1000);
                Check.True(
                    rig.Nodes[0].Session.GetNetworkStats(1).RoundTripMilliseconds > 700,
                    "Startup scheduling delay inflates the measured RTT"
                );
                Check.True(
                    rig.Nodes.All(n => n.Session.ExtraDelayFrames == 0),
                    "A stale RTT estimate alone cannot add delay while prediction has room"
                );
                rig.Run(4000);
            }
            else
                rig.Run(5000);
            Check.True(
                rig.Nodes.All(n => n.Session.ExtraDelayFrames == 0),
                $"{scenario}: startup settles without persistent extra delay at 400 ms RTT"
            );
            if (scenario == "one pause")
                rig.PauseNode = (id, time) => id == 1 && time < 5500;
            if (scenario == "both pause")
                rig.PauseNode = (_, time) => time < 5500;
            if (scenario == "background")
                rig.Nodes[1].RenderHz = 15;
            if (scenario == "latency step")
                rig.Network.Latency = 250;
            rig.Run(3000);
            rig.PauseNode = null;
            rig.Nodes[1].RenderHz = 60;
            rig.Network.Latency = 200;
            rig.Run(3000);
            Check.True(
                rig.Nodes.All(n => n.Session.ExtraDelayFrames == 0),
                $"{scenario}: temporary extra delay clears within three seconds of recovery"
            );
            int before = rig.MinimumFrame;
            rig.Run(3000);
            Check.True(rig.MinimumFrame - before >= 350, $"{scenario}: normal speed returns after recovery");
            Check.True(
                rig.Nodes.All(n => n.Session.ExtraDelayFrames == 0),
                $"{scenario}: peers do not sustain an extra-delay oscillation"
            );
            rig.CheckAgreement($"adaptive {scenario}");
        }
    }

    private static void AdaptiveInputContinuity()
    {
        var rig = Create([0, 0]);
        var node = rig.Nodes[0];
        node.RenderHz = 1000;
        node.Session.RestoreExtraDelay(30);
        int[] previousAccepted = [-1, -1];
        int longestGap = 0;
        for (int time = 0; time < 2000; time++)
        {
            rig.Run(1);
            foreach (int handle in node.Session.LocalPlayerHandles)
            {
                int accepted = node.Session.LastAcceptedInputFrame(handle);
                longestGap = Math.Max(longestGap, accepted - previousAccepted[handle]);
                previousAccepted[handle] = accepted;
            }
        }
        Check.Equal(0, node.Session.ExtraDelayFrames, "Thirty restored delay ticks drain within two seconds");
        Check.True(longestGap <= 2, "Fast recovery never skips more than one consecutive local input sample");
        rig.CheckAgreement("adaptive local input continuity");

        rig = Create();
        rig.Network.Latency = 200;
        rig.Run(5000);
        rig.PauseNode = (id, _) => id == 1;
        rig.Run(800);
        node = rig.Nodes[0];
        int frozenFrame = node.Session.CurrentFrame;
        int frozenDelay = node.Session.ExtraDelayFrames;
        rig.Run(500);
        Check.Equal(frozenFrame, node.Session.CurrentFrame, "The unavailable peer exhausts prediction");
        Check.Equal(frozenDelay, node.Session.ExtraDelayFrames, "Polling cannot keep increasing unapplied delay");
        rig.PauseNode = null;
        rig.Network.Latency = 20;
        rig.Run(4000);
        Check.True(rig.Nodes.All(n => n.Session.ExtraDelayFrames == 0), "Stalled peers resume and shed extra delay");
        rig.CheckAgreement("adaptive stalled input continuity");
    }

    private static void PauseAtLimit()
    {
        var rig = Create();
        foreach (var node in rig.Nodes)
            node.Session.SetTiming(new());
        rig.Network.Latency = 500;
        rig.Run(10000);
        int before = rig.MinimumFrame;
        rig.Run(4000);
        Check.True(rig.MinimumFrame - before < 350, "Zero extra-delay allowance pauses when prediction is exhausted");
        Check.True(rig.Nodes.All(n => n.Session.ExtraDelayFrames == 0), "Pause policy never silently adds delay");
        rig.CheckAgreement("pause at prediction limit");
    }

    private static void MultiplePeers()
    {
        var rig = Create([0, 1, 2, 3]);
        rig.Network.Latency = 70;
        rig.Network.Jitter = 15;
        rig.Network.Loss = .02;
        for (int id = 0; id < rig.Nodes.Count; id++)
            rig.Nodes[id].Session.SetTiming(new() { DelayFrames = id * 4, DonationFrames = id * 2 });
        rig.Run(15000);
        int before = rig.MinimumFrame;
        rig.Run(5000);
        Check.True(rig.MinimumFrame - before >= 580, "Unequal preferences preserve normal multi-peer throughput");
        int[] horizons = rig
            .Nodes.Select(n =>
                n.Session.CurrentFrame + n.Session.EffectiveDelayFrames - n.Session.Timing.DonationFrames
            )
            .ToArray();
        Check.True(
            horizons.Max() - horizons.Min() <= 6,
            "Multi-peer pacing aligns input horizons after personal timing offsets"
        );
        rig.CheckAgreement("multi-peer personal preferences");
    }
}
