using System.Diagnostics;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--udp-peer")
            {
                return UdpProcessTests.UdpPeer(args);
            }

            if (args.Length > 1 && args[0] == "--describe-build")
            {
                BuildDiagnostics.DescribeBuild(args[1]);
                return 0;
            }

            if (args.Contains("--replay-hash"))
            {
                var replay = MakeWorld(8);
                for (int tick = 0; tick < 2400; tick++)
                {
                    replay.Tick(Enumerable.Range(0, 8).Select(s => Input(tick, s)).ToArray());
                }

                Console.WriteLine($"FROG_REPLAY_V1 ticks=2400 players=8 hash={replay.HashState():x16}");
                return 0;
            }

            if (args.Contains("--benchmark"))
            {
                BuildDiagnostics.Benchmark();
                return 0;
            }

            if (args.Length > 0 && args[0] == "--steam-probe")
            {
                using var client = SteamClient.Connect();
                client.Poll();
                if (!client.Available)
                {
                    Console.WriteLine(client.Error);
                    return 0;
                }

                using (var lobby = SteamLobby.Host(2, [new(0, Spawned: true)], "probe", "{}"))
                {
                    lobby.Poll();
                    Console.WriteLine(lobby.Error ?? $"Steam initialized: {lobby.Status}");
                }

                client.Poll();
                Console.WriteLine($"Menu runtime remains available after lobby disposal: {client.Available}");
                return 0;
            }

            var timer = Stopwatch.StartNew();
            ReplayTests.ReplayAndSnapshots();
            PhysicsTests.FixedArithmetic();
            PhysicsTests.Movement();
            PhysicsTests.ContentTuning();
            PhysicsTests.Spawns();
            PhysicsTests.LongRunningClock();
            CombatTests.BatHitsAndHitstop();
            CombatTests.TongueAttacks();
            CombatTests.FlyClaims();
            CombatTests.StrafeInputs();
            CombatTests.StrafingTongueAim();
            CombatTests.TongueCollisions();
            MatchTests.Scoring();
            MatchTests.RoundProgression();
            BotNavigationTests.PlatformsAndCombat();
            ContentSimulationTests.AuthoredMaps();
            InputReplayTests.RoundTrip();
            foreach (int peers in new[] { 2, 4, 8 })
            {
                NetworkSessionTests.ImpairedNetwork(peers, false);
            }

            NetworkSessionTests.ImpairedNetwork(4, true);
            NetworkSessionTests.ClockDrift();
            NetworkSessionTests.RenderRateTraffic();
            NetworkSessionTests.TerminalBarrier();
            NetworkProtocolTests.PacketValidationAndDesync();
            NetworkProtocolTests.PacketStructure();
            LobbyTests.Run(Check);
            if (!args.Contains("--no-sockets"))
            {
                UdpProcessTests.LocalhostProcesses();
            }

            Console.WriteLine($"PASS: {TestAssert.Count} checks in {timer.Elapsed.TotalSeconds:F2}s");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            return 1;
        }
    }
}
