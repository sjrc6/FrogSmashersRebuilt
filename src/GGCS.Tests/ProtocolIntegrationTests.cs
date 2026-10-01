using GGCS.Protocol;

namespace GGCS.Tests;

internal static class ProtocolIntegrationTests
{
    public static void Run()
    {
        AsymmetricStatusDelayDoesNotExpelSurvivingPlayers();
        ObserverProtocolFailuresCannotEndThePlayingSession();
        OverlappingRemovalsConvergeWithEightPeersAndSpectators();
    }

    private static void AsymmetricStatusDelayDoesNotExpelSurvivingPlayers()
    {
        var rig = new SessionRig([0, 1, 2]);
        rig.Network.Latency = 10;
        rig.Run(1500);
        rig.Network.Block = (from, to, _) => from == 1 && to == 0;
        rig.Run(20);
        rig.Nodes[0].Session.DisconnectPeer(2);
        rig.Network.Block = null;
        rig.Run(1500);

        var survivors = rig.Nodes.Take(2).ToArray();
        foreach (var node in survivors)
        {
            Check.Equal(
                SessionState.Running,
                node.Session.State,
                "A delayed status report must not expel an unrelated survivor"
            );
            Check.True(
                !node.Events.Any(e => e.Kind is SessionEventKind.ProtocolError or SessionEventKind.DesyncDetected),
                "Disconnect agreement must preserve already-finalized history"
            );
        }
        int common = survivors.Min(node => node.Session.ConfirmedFrame) + 1;
        Check.True(common > 250, "Survivors recover ordinary advancement after asymmetric disconnect reports");
        Check.Equal(
            survivors[0].Game.States[common],
            survivors[1].Game.States[common],
            "Survivors agree about the disconnected player's final input"
        );
        Check.Equal(
            SessionState.Disconnected,
            rig.Nodes[2].Session.State,
            "The excluded machine stops its old session"
        );
    }

    private static void ObserverProtocolFailuresCannotEndThePlayingSession()
    {
        foreach (bool rawObserver in new[] { false, true })
        {
            foreach (bool badConfiguration in new[] { false, true })
            {
                var rig = new SessionRig([0, 1]);
                if (rawObserver)
                    rig.Nodes[0].Session.AddInputObserver(100);
                else
                    rig.AddSpectator(100, 0);
                rig.Run(1000);
                var wire = new SessionWire<int>(rig.Players, new IntCodec(), rig.Options, "");
                ulong configuration = badConfiguration ? wire.ConfigurationId ^ 1 : wire.ConfigurationId;
                byte[] packet = PacketCodec.Encode(
                    1,
                    configuration,
                    new ProtocolPacket { Kind = PacketKind.Disconnect, ExcludeRemote = true }
                );
                rig.Network.Inject(100, 0, packet);
                rig.Run(1000);

                foreach (var node in rig.Nodes)
                    Check.Equal(
                        SessionState.Running,
                        node.Session.State,
                        "Nonplaying peer errors cannot close active player sessions"
                    );
                int common = rig.Nodes.Min(node => node.Session.ConfirmedFrame) + 1;
                Check.True(common > 200, "Active gameplay continues after dropping a broken observer");
                Check.Equal(
                    rig.Nodes[0].Game.States[common],
                    rig.Nodes[1].Game.States[common],
                    "Observer failure cannot change active player inputs or simulation history"
                );
            }
        }
    }

    private static void OverlappingRemovalsConvergeWithEightPeersAndSpectators()
    {
        for (int seed = 0; seed < 8; seed++)
        {
            var random = new Random(seed + 440);
            var rig = new SessionRig(Enumerable.Range(0, 8).ToArray(), seed: seed + 98);
            rig.Network.LinkLatency = (from, to) => 5 + ((from * 13 + to * 7 + seed * 11) % 5) * 10;
            rig.Network.Jitter = 10;
            rig.Network.Loss = .03;
            rig.Network.Duplication = .02;
            for (int spectator = 100; spectator < 104; spectator++)
                rig.AddSpectator(spectator, 0);
            rig.Run(2500);
            int startingFrame = rig.MinimumFrame;
            int[] removed = new[] { 5, 6, 7 }.OrderBy(_ => random.Next()).Take(2 + seed % 2).ToArray();
            for (int index = 0; index < removed.Length; index++)
            {
                rig.Nodes[index].Session.DisconnectPeer(removed[index]);
                rig.Run(seed % 3 == 0 ? 1 : random.Next(40, 250));
            }
            rig.Run(3500);
            var survivors = rig.Nodes.Where(node => !removed.Contains(node.Id)).ToArray();
            foreach (var node in survivors)
            {
                Check.Equal(
                    SessionState.Running,
                    node.Session.State,
                    $"Seed {seed}: overlapping removals preserve survivor {node.Id}"
                );
                Check.True(
                    !node.Events.Any(e => e.Kind is SessionEventKind.ProtocolError or SessionEventKind.DesyncDetected),
                    $"Seed {seed}: survivor {node.Id} agrees on frozen history: {string.Join(", ", node.Events.Where(e => e.Kind is SessionEventKind.ProtocolError or SessionEventKind.DesyncDetected))}"
                );
            }
            int common = Math.Min(
                survivors.Min(node => node.Session.ConfirmedFrame) + 1,
                rig.Spectators.Min(spectator => spectator.Session.CurrentFrame)
            );
            Check.True(
                common > startingFrame + 200,
                $"Seed {seed}: the remaining lobby recovers promptly after removal agreement"
            );
            ulong expected = survivors[0].Game.States[common];
            foreach (var node in survivors)
                Check.Equal(
                    expected,
                    node.Game.States[common],
                    $"Seed {seed}: survivor {node.Id} retains the same committed state"
                );
            foreach (var spectator in rig.Spectators)
                Check.Equal(
                    expected,
                    spectator.Game.States[common],
                    $"Seed {seed}: spectator {spectator.Id} never receives rewritten confirmed history"
                );
            foreach (int peerId in removed)
                Check.Equal(
                    SessionState.Disconnected,
                    rig.Nodes[peerId].Session.State,
                    $"Seed {seed}: excluded machine {peerId} stops its old generation"
                );
        }
    }
}
