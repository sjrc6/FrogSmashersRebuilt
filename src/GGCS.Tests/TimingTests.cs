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
        return rig;
    }

    public static void Run()
    {
        PersonalDelay();
        Donation();
        PauseAtLimit();
        MultiplePeers();
        FrozenTiming();
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
        session.SetTiming(new() { DelayFrames = 7, DonationFrames = 3 });
        Check.Equal(2, session.Timing.DelayFrames, "Timing remains stable during a checkpoint barrier");
        session.FreezeTiming(false);
        Check.Equal(7, session.Timing.DelayFrames, "Deferred personal timing applies after checkpoint cancellation");
        rig.Run(1000);
        rig.CheckAgreement("timing changes around a pause barrier");
    }

    private static void PersonalDelay()
    {
        var rig = Create();
        rig.Network.Latency = 100;
        rig.Nodes[0].Session.SetTiming(new() { DelayFrames = 0 });
        rig.Nodes[1].Session.SetTiming(new() { DelayFrames = 12 });
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
        rig.Nodes[0].Session.SetTiming(new() { DonationFrames = 10 });
        rig.Nodes[1].Session.SetTiming(new());
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

    private static void PauseAtLimit()
    {
        var rig = Create();
        foreach (var node in rig.Nodes)
            node.Session.SetTiming(new());
        rig.Network.Latency = 500;
        rig.Run(10000);
        int before = rig.MinimumFrame;
        rig.Run(4000);
        Check.True(rig.MinimumFrame - before < 350, "Prediction exhaustion pauses advancement");
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
            .Nodes.Select(n => n.Session.CurrentFrame + n.Session.Timing.DelayFrames - n.Session.Timing.DonationFrames)
            .ToArray();
        Check.True(
            horizons.Max() - horizons.Min() <= 6,
            "Multi-peer pacing aligns input horizons after personal timing offsets"
        );
        rig.CheckAgreement("multi-peer personal preferences");
    }
}
