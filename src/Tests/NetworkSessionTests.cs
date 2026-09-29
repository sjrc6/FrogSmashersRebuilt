using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class NetworkSessionTests
{
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
                return new NetworkSession(w, new SessionConfig(slots, p, "test-content", w), network.Endpoint(p));
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
                return new NetworkSession(w, new SessionConfig(slots, p, "clock", w), network.Endpoint(p));
            })
            .ToArray();
        int pauses = 0;
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
                int attempts = p == 0 ? (wall % 17 == 0 ? 2 : 1) : (wall % 23 == 0 ? 0 : 1);
                for (int n = 0; n < attempts && s.World.TickNumber < 900; n++)
                {
                    if (s.ShouldWaitForPeers)
                    {
                        pauses++;
                        break;
                    }

                    s.TryAdvance([Input(s.World.TickNumber, p)]);
                }
            }

            maxLead = Math.Max(maxLead, (int)Math.Abs(sessions[0].World.TickNumber - sessions[1].World.TickNumber));
        }

        Check(
            sessions.All(s => s.World.TickNumber == 900 && s.ConfirmedFrame == 899),
            "Clock drift pacing failed to converge"
        );
        Check(sessions[0].World.HashState() == sessions[1].World.HashState(), "Clock drift changed simulation");
        Check(pauses > 0 && maxLead < 24, "Pacing did not bound clock lead");
        Console.WriteLine($"Clock drift: bounded lead {maxLead} ticks, {pauses} pace waits");
        foreach (var s in sessions)
        {
            s.Dispose();
        }
    }

    public static void RenderRateTraffic()
    {
        const int peers = 8;
        const int target = 240;
        var network = new SimulatedNetwork(peers, 45, 1, 0, 0, 0);
        var slots = Enumerable.Range(0, peers).Select(p => new[] { p }).ToArray();
        var sessions = Enumerable
            .Range(0, peers)
            .Select(p =>
            {
                var w = MakeWorld(peers);
                return new NetworkSession(w, new SessionConfig(slots, p, "fps", w), network.Endpoint(p));
            })
            .ToArray();
        for (int tick = 0; tick < target; tick++)
        {
            network.Advance();
            for (int render = 0; render < 16; render++)
            {
                foreach (var s in sessions)
                {
                    s.Poll();
                }
            }

            foreach (var s in sessions)
            {
                checkAdvance(s);
            }
        }

        void checkAdvance(NetworkSession s)
        {
            Check(s.TryAdvance([Input(s.World.TickNumber, s.LocalPeer)]), "High-render-rate session stalled");
            Check(s.Error == null, s.Error ?? "High-FPS network error");
        }

        Check(network.PacketsSent <= peers * (peers - 1) * (target + 2), "Render polling multiplied network send rate");
        Console.WriteLine(
            $"Render-rate isolation: 16 polls/tick, {network.PacketsSent} packets for 8 peers/{target} ticks"
        );
        foreach (var s in sessions)
        {
            s.Dispose();
        }
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
                return new NetworkSession(w, new SessionConfig(slots, p, "terminal", w), network.Endpoint(p));
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
