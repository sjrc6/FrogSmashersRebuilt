using GGCS.Protocol;

namespace GGCS.Tests;

internal static class ReviewTests
{
    public static void Run()
    {
        DelayChangesDuringPredictionStalls();
        SimultaneousDisconnects();
        TimedOutPeerDoesNotHoldSurvivors();
        MissingObserversNeverHoldPlayers();
        RenderHitchesRecover();
        ClosingSessionStopsNetworkAndSimulation();
        UnknownSourcesCannotChangeTheSession();
        ExcessiveFutureInputFailsWithoutThrowing();
        MalformedSpectatorCannotStopPlayers();
        StaleReceiptReportsCannotInvalidateAnotherPeersCommit();
        ObservingHostSurvivesAnAgreedPlayerDeparture();
        BufferedSpectatorCanConsumeTheFinalFrame();
        SpectatorCheckpointAfterAPlayerDisconnected();
        SequentialDeparturesKeepEarlierCutoffsFixed();
        LostObserverLinkDoesNotChangeTheActiveRoster();
    }

    private static void DelayChangesDuringPredictionStalls()
    {
        var rig = new SessionRig(
            [0, 0, 1, 1],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 8,
                InputDelay = 2,
                HistoryFrames = 120,
                MaxInputDelay = 12,
            }
        );
        rig.Network.Latency = 20;
        rig.Run(1500);

        for (int repeat = 0; repeat < 4; repeat++)
        {
            long outage = rig.Clock.NowMilliseconds;
            rig.Network.Block = (_, _, time) => time < outage + 400;
            rig.Run(300);
            int stalledFrame = rig.Nodes[0].Session.CurrentFrame;
            rig.Nodes[0].Session.SetTiming(new() { DelayFrames = repeat % 2 == 0 ? 12 : 0, MaxExtraDelayFrames = 0 });
            rig.Nodes[1].Session.SetTiming(new() { DelayFrames = repeat % 2 == 0 ? 3 : 0, MaxExtraDelayFrames = 0 });
            rig.Run(50);
            Check.Equal(
                stalledFrame,
                rig.Nodes[0].Session.CurrentFrame,
                "Changing delay does not resubmit a stalled frame"
            );
            rig.Run(1400);
            rig.CheckAgreement($"delay change during outage {repeat}");
        }
        rig.Network.Block = null;
        int recovered = rig.MinimumFrame;
        rig.Run(1500);
        Check.True(rig.MinimumFrame - recovered >= 170, "Delay changes while stalled preserve recovery speed");
    }

    private static void SimultaneousDisconnects()
    {
        var rig = new SessionRig([0, 1, 2, 2, 3, 3]);
        rig.Network.Latency = 25;
        rig.Network.Jitter = 10;
        rig.Run(2200);
        long cutoff = rig.Clock.NowMilliseconds;
        rig.Network.Block = (from, to, time) => time >= cutoff && ((from == 2 && to == 0) || (from == 3 && to == 1));
        rig.Run(100);
        rig.Nodes[0].Session.DisconnectPeer(2);
        rig.Nodes[1].Session.DisconnectPeer(3);
        rig.Run(1600);

        var survivors = rig.Nodes.Where(n => n.Id < 2).ToArray();
        int common = survivors.Min(n => n.Session.ConfirmedFrame) + 1;
        Check.True(common > 350, "Two simultaneous disconnects leave surviving machines advancing");
        Check.Equal(
            survivors[0].Game.States[common],
            survivors[1].Game.States[common],
            "Survivors agree after two machines with multiple local players leave"
        );
        foreach (var survivor in survivors)
            Check.True(
                !survivor.Events.Any(e => e.Kind is SessionEventKind.DesyncDetected or SessionEventKind.ProtocolError),
                "Simultaneous disconnects preserve finalized history"
            );
        foreach (var removed in rig.Nodes.Where(n => n.Id >= 2))
            Check.Equal(
                SessionState.Disconnected,
                removed.Session.State,
                "Excluded machines stop their old generation"
            );
    }

    private static void TimedOutPeerDoesNotHoldSurvivors()
    {
        var rig = new SessionRig(
            [0, 1, 2],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 12,
                HistoryFrames = 120,
                DisconnectNotifyMilliseconds = 150,
                DisconnectTimeoutMilliseconds = 600,
            }
        );
        rig.Network.Latency = 20;
        rig.Run(1500);
        long outage = rig.Clock.NowMilliseconds;
        rig.Network.Block = (from, to, time) => time >= outage && (from == 2 || to == 2);
        rig.PauseNode = (id, time) => id == 2 && time >= outage;
        rig.Run(2500);
        var survivors = rig.Nodes.Take(2).ToArray();
        int common = survivors.Min(n => n.Session.ConfirmedFrame) + 1;
        Check.True(common > 350, "Timeout releases surviving players from the prediction limit");
        Check.Equal(
            survivors[0].Game.States[common],
            survivors[1].Game.States[common],
            "Timeout agrees on discarded player's final input"
        );
        foreach (var node in survivors)
        {
            Check.True(
                node.Events.Any(e => e.PeerId == 2 && e.Kind == SessionEventKind.NetworkInterrupted),
                "Timeout first reports interruption"
            );
            Check.Equal(
                SessionState.Disconnected,
                node.Session.GetNetworkStats(2).State,
                "Timed-out endpoint stays disconnected"
            );
            Check.True(
                node.Session.GetNetworkStats(2).PendingInputFrames <= rig.Options.InputCapacity,
                "Timed-out endpoint retains bounded input memory"
            );
        }
    }

    private static void MissingObserversNeverHoldPlayers()
    {
        var rig = new SessionRig(
            [0, 1],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 12,
                HistoryFrames = 60,
                MaxInputDelay = 8,
                SpectatorBufferFrames = 0,
            }
        );
        foreach (var node in rig.Nodes)
            node.Session.AddInputObserver(50);
        rig.Nodes[0].Session.AddSpectator(100);
        rig.Run(3000);
        Check.True(rig.MinimumFrame >= 340, "Absent observers and spectators never block active play");
        rig.CheckAgreement("absent observers");
        foreach (var node in rig.Nodes)
            Check.True(
                node.Events.Any(e => e.Kind == SessionEventKind.SpectatorTooFarBehind && e.PeerId == 50),
                "Unresponsive input observer is removed when its bounded buffer fills"
            );
    }

    private static void RenderHitchesRecover()
    {
        var rig = new SessionRig([0, 1, 2]);
        rig.Network.Latency = 25;
        rig.Network.Jitter = 12;
        rig.Network.Loss = .02;
        rig.Run(2000);
        long begin = rig.Clock.NowMilliseconds;
        rig.PauseNode = (id, time) => id == 1 && time - begin < 4500 && (time - begin) % 1000 < 180;
        rig.Run(5000);
        rig.PauseNode = null;
        rig.Run(1500);
        int recovered = rig.MinimumFrame;
        rig.Run(2000);
        Check.True(
            rig.MinimumFrame - recovered >= 232,
            "Repeated render hitches do not leave pacing permanently slowed"
        );
        Check.True(
            rig.Nodes.Max(n => n.Session.CurrentFrame) - rig.MinimumFrame < 20,
            "Peers regroup after render hitches"
        );
        rig.CheckAgreement("render hitch recovery");
    }

    private static void ClosingSessionStopsNetworkAndSimulation()
    {
        var rig = new SessionRig([0, 1]);
        rig.Run(500);
        foreach (var node in rig.Nodes)
            node.Session.Close();
        long packets = rig.Network.Packets;
        int[] frames = rig.Nodes.Select(n => n.Session.CurrentFrame).ToArray();
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            foreach (var node in rig.Nodes)
            {
                node.Session.Poll();
                Check.Equal(
                    AdvanceStatus.Disconnected,
                    node.Session.AdvanceFrame([123]),
                    "Closed sessions reject advancement"
                );
                node.Session.Close();
            }
            rig.Clock.NowMilliseconds += 10;
        }
        Check.Equal(packets, rig.Network.Packets, "Closed sessions do not retain protocol resend work");
        for (int index = 0; index < frames.Length; index++)
            Check.Equal(
                frames[index],
                rig.Nodes[index].Session.CurrentFrame,
                "Closed sessions keep their last simulation state"
            );
    }

    private static void UnknownSourcesCannotChangeTheSession()
    {
        var rig = new SessionRig([0, 1]);
        rig.Network.CapturePackets = true;
        rig.Run(1500);
        byte[][] replay = rig.Network.Capture.Where(p => p.From == 1 && p.To == 0).Select(p => p.Data).ToArray();
        rig.Network.CapturePackets = false;
        foreach (byte[] packet in replay)
            rig.Network.Inject(999, 0, packet);
        rig.Run(1500);
        rig.CheckAgreement("unknown source packets");
        Check.True(
            rig.Nodes[0].Session.CurrentFrame > 320,
            "Unknown source traffic cannot mutate roster or delay progress"
        );
    }

    private static void ExcessiveFutureInputFailsWithoutThrowing()
    {
        var rig = new SessionRig(
            [0, 1],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 12,
                MaxInputDelay = 0,
                HistoryFrames = 60,
                SpectatorBufferFrames = 0,
            }
        );
        rig.Run(2000);
        var target = rig.Nodes[0].Session;
        var codec = new IntCodec();
        var wire = new SessionWire<int>(rig.Players, codec, rig.Options, "");
        int start = target.GetNetworkStats(1).LastReceivedFrame + 1;
        byte[][] frames = Enumerable.Range(0, 80).Select(_ => new byte[4]).ToArray();
        var packet = new ProtocolPacket
        {
            Kind = PacketKind.Inputs,
            Frame = target.CurrentFrame,
            Advantage = 0,
            AcknowledgedFrame = target.GetNetworkStats(1).LastAcknowledgedFrame,
            Statuses = rig.Players.Select(_ => new ConnectionStatus(false, target.CurrentFrame - 1)).ToArray(),
            StartFrame = start,
            InputCount = frames.Length,
            InputData = InputCompression.Encode(frames, 0, frames.Length, 4),
        };
        rig.Network.Inject(1, 0, PacketCodec.Encode(1, wire.ConfigurationId, packet));
        target.Poll();
        Check.Equal(
            SessionState.Disconnected,
            target.State,
            "Excessive future input fails the session without throwing from Poll"
        );
        bool reported = false;
        while (target.TryGetEvent(out var item))
            reported |= item.Kind == SessionEventKind.ProtocolError;
        Check.True(reported, "Input retention overflow produces a protocol diagnostic");
    }

    private static void MalformedSpectatorCannotStopPlayers()
    {
        foreach (bool observer in new[] { false, true })
        foreach (bool exclude in new[] { false, true })
        {
            var rig = new SessionRig([0, 1]);
            if (observer)
                rig.Nodes[0].Session.AddInputObserver(100);
            else
                rig.Nodes[0].Session.AddSpectator(100);
            rig.Run(1000);
            var wire = new SessionWire<int>(rig.Players, new IntCodec(), rig.Options, "");
            var invalidHandshake = new ProtocolPacket
            {
                Kind = exclude ? PacketKind.Disconnect : PacketKind.Synchronize,
                ExcludeRemote = exclude,
                Challenge = 42,
                SendInputSize = 4,
                ReceiveInputSize = observer ? wire.InputSize : wire.SpectatorInputSize,
                PlayerCount = rig.Players.Length,
                InitialFrame = 0,
            };
            rig.Network.Inject(100, 0, PacketCodec.Encode(1, wire.ConfigurationId, invalidHandshake));
            rig.Nodes[0].Session.Poll();
            Check.Equal(
                SessionState.Running,
                rig.Nodes[0].Session.State,
                "Malformed observer traffic only disconnects that observer"
            );
            int before = rig.MinimumFrame;
            rig.Run(1000);
            Check.True(rig.MinimumFrame - before >= 110, "Observer protocol failures never stop active simulation");
        }
    }

    private static void StaleReceiptReportsCannotInvalidateAnotherPeersCommit()
    {
        var rig = new SessionRig([0, 1, 2]);
        rig.Network.Latency = 10;
        rig.Run(2000);
        long begin = rig.Clock.NowMilliseconds;
        rig.Network.Block = (from, to, time) => from == 1 && to == 0 && time < begin + 300;
        rig.Run(140);
        int committedAtPeerOne = rig.Nodes[1].Session.ConfirmedFrame;
        Check.True(
            committedAtPeerOne > rig.Nodes[0].Session.ConfirmedFrame,
            "The disconnect test reaches asymmetric confirmation with stale receipt reports"
        );
        rig.Nodes[0].Session.DisconnectPeer(2);
        rig.Run(1800);

        var survivors = rig.Nodes.Take(2).ToArray();
        int agreed = survivors.Min(n => n.Session.ConfirmedFrame) + 1;
        Check.True(
            agreed > committedAtPeerOne + 120,
            "Survivors resume after agreeing a cutoff above both previous commits"
        );
        Check.Equal(
            survivors[0].Game.States[agreed],
            survivors[1].Game.States[agreed],
            "Stale receipt reports cannot change another survivor's committed state"
        );
        foreach (var survivor in survivors)
            Check.True(
                !survivor.Events.Any(e => e.Kind is SessionEventKind.DesyncDetected or SessionEventKind.ProtocolError),
                "Disconnect agreement never asks to repair an immutable frame"
            );
    }

    private static void ObservingHostSurvivesAnAgreedPlayerDeparture()
    {
        var rig = new SessionRig([1, 2, 3]);
        foreach (var node in rig.Nodes)
            node.Session.AddInputObserver(0);
        var host = rig.AddNode(0);
        rig.AddSpectator(100, 0);
        rig.Network.Latency = 20;
        rig.Run(2500);
        int before = host.Session.CurrentFrame;
        rig.Nodes.Single(n => n.Id == 1).Session.DisconnectPeer(3);
        rig.Run(1800);

        Check.Equal(
            SessionState.Running,
            host.Session.State,
            "An observing host follows an agreed departure without restarting"
        );
        Check.True(
            host.Session.CurrentFrame > before + 120,
            "The observing host advances after the disconnect barrier"
        );
        int common = Math.Min(host.Session.ConfirmedFrame, rig.Nodes[0].Session.ConfirmedFrame) + 1;
        Check.Equal(
            rig.Nodes[0].Game.States[common],
            host.Game.States[common],
            "Observing host adopts the active peers' agreed cutoff"
        );
        var spectator = rig.Spectators[0];
        Check.True(
            spectator.Session.CurrentFrame > before + 100,
            "Downstream spectators keep receiving host inputs after departure"
        );
        Check.Equal(
            host.Game.States[spectator.Session.CurrentFrame],
            spectator.Game.Value,
            "Downstream spectator sees the same disconnected player state as active peers"
        );
    }

    private static void BufferedSpectatorCanConsumeTheFinalFrame()
    {
        var rig = new SessionRig(
            [0, 1],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 24,
                HistoryFrames = 120,
                SpectatorBufferFrames = 4,
            }
        );
        rig.AddSpectator(100, 0);
        rig.StopAtFrame = 300;
        rig.Run(4000);
        var spectator = rig.Spectators[0];
        Check.Equal(
            296,
            spectator.Session.CurrentFrame,
            "Configured spectator buffering keeps its requested presentation delay"
        );
        while (spectator.Session.AdvanceAvailable(drain: true) > 0) { }
        Check.Equal(
            300,
            spectator.Session.CurrentFrame,
            "Explicit drain renders the final committed frame before a pause or transition"
        );
        Check.Equal(
            rig.Nodes[0].Game.States[300],
            spectator.Game.Value,
            "Final spectator state agrees with the stopped host"
        );
    }

    private static void SpectatorCheckpointAfterAPlayerDisconnected()
    {
        var rig = new SessionRig([0, 1, 2]);
        rig.Network.Latency = 10;
        rig.Run(1500);
        rig.Nodes[0].Session.DisconnectPeer(2);
        rig.Run(1600);
        var host = rig.Nodes[0];
        int start = host.Session.ConfirmedFrame - 30;
        Check.True(
            start > rig.Nodes[2].Session.CurrentFrame,
            "The spectator checkpoint follows the removed player's last frame"
        );
        rig.AddSpectator(100, 0, start, host.Game.States[start]);
        rig.Run(1800);
        var spectator = rig.Spectators[0];
        Check.Equal(
            SessionState.Running,
            spectator.Session.State,
            "Historical disconnect cutoffs are valid after a later spectator checkpoint"
        );
        Check.True(spectator.Session.CurrentFrame > start + 160, "Late spectator catches up after a roster departure");
        Check.Equal(
            host.Game.States[spectator.Session.CurrentFrame],
            spectator.Game.Value,
            "Late spectator applies the retained disconnected-player stream exactly"
        );
    }

    private static void SequentialDeparturesKeepEarlierCutoffsFixed()
    {
        var rig = new SessionRig([0, 1, 2, 3]);
        rig.Network.Latency = 20;
        rig.Run(1500);
        rig.Nodes[0].Session.DisconnectPeer(3);
        rig.Run(1000);
        int alreadyCommitted = rig.Nodes.Take(3).Min(n => n.Session.ConfirmedFrame);
        ulong committedState = rig.Nodes[0].Game.States[alreadyCommitted + 1];
        Check.True(
            rig.Nodes[0].Session.TryGetConfirmedInputs(alreadyCommitted, out var before),
            "Earlier departure remains in retained input history"
        );
        Check.Equal(InputStatus.Disconnected, before[3].Status, "First removed player already has disconnected input");
        Check.Equal(InputStatus.Confirmed, before[2].Status, "Second player is still active before its departure");

        rig.Nodes[1].Session.DisconnectPeer(2);
        rig.Run(1200);
        foreach (var survivor in rig.Nodes.Take(2))
        {
            Check.Equal(
                committedState,
                survivor.Game.States[alreadyCommitted + 1],
                "Later membership agreement never changes earlier published state"
            );
            Check.True(
                survivor.Session.TryGetConfirmedInputs(alreadyCommitted, out var retained),
                "Earlier committed inputs remain readable after a second departure"
            );
            Check.Equal(
                InputStatus.Disconnected,
                retained[3].Status,
                "Cumulative disconnect masks do not move an earlier cutoff forward"
            );
            Check.Equal(
                InputStatus.Confirmed,
                retained[2].Status,
                "A later departure does not move its cutoff before prior confirmation"
            );
            Check.True(
                survivor.Session.TryGetConfirmedInputs(survivor.Session.ConfirmedFrame, out var latest),
                "Latest committed frame exists after repeated departures"
            );
            Check.Equal(InputStatus.Disconnected, latest[2].Status, "Second departure takes effect after agreement");
            Check.Equal(InputStatus.Disconnected, latest[3].Status, "First departure stays in effect after agreement");
        }
    }

    private static void LostObserverLinkDoesNotChangeTheActiveRoster()
    {
        var rig = new SessionRig(
            [1, 2],
            new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 24,
                HistoryFrames = 240,
                DisconnectNotifyMilliseconds = 150,
                DisconnectTimeoutMilliseconds = 600,
                SpectatorBufferFrames = 0,
            }
        );
        foreach (var player in rig.Nodes)
            player.Session.AddInputObserver(0);
        var host = rig.AddNode(0);
        rig.AddSpectator(100, 0);
        rig.Network.Latency = 20;
        rig.Run(1800);
        int activeBefore = rig.Nodes.Take(2).Min(n => n.Session.CurrentFrame);
        rig.Network.Block = (from, to, _) => (from == 1 && to == 0) || (from == 0 && to == 1);
        rig.Run(2000);

        foreach (var player in rig.Nodes.Take(2))
        {
            Check.Equal(
                SessionState.Running,
                player.Session.State,
                "Broken observer links leave the active mesh running"
            );
            Check.True(
                player.Session.CurrentFrame > activeBefore + 220,
                "An observer cannot slow healthy active peers"
            );
            Check.True(
                player.Session.TryGetConfirmedInputs(player.Session.ConfirmedFrame, out var inputs),
                "Healthy mesh still publishes final inputs"
            );
            Check.True(
                inputs.All(input => input.Status == InputStatus.Confirmed),
                "Observer timeout cannot remove a healthy active player"
            );
        }
        Check.Equal(
            SessionState.Disconnected,
            host.Session.State,
            "Observer without a complete input stream eventually requests a new checkpoint"
        );
        Check.True(
            host.Events.Any(e => e.Kind == SessionEventKind.ProtocolError),
            "Observer reports why its stream cannot continue"
        );
        var spectator = rig.Spectators[0];
        Check.Equal(
            rig.Nodes[0].Game.States[spectator.Session.CurrentFrame],
            spectator.Game.Value,
            "Downstream spectator never consumes a fabricated disconnected-player frame"
        );
    }
}
