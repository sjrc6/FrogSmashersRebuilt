using System.Diagnostics;
using FrogSmashers.Core;
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
            if (args.Contains("--discovery"))
            {
                LobbyDiscoveryTests.Run(!args.Contains("--no-sockets"));
                Console.WriteLine($"PASS: {TestAssert.Count} lobby discovery checks");
                return 0;
            }
            if (args.Contains("--lobby"))
            {
                LobbyTests.Run(Check);
                Console.WriteLine($"PASS: {TestAssert.Count} lobby checks");
                return 0;
            }
            if (args.Contains("--live-lobby"))
            {
                LobbyTests.RunLive();
                Console.WriteLine($"PASS: {TestAssert.Count} live lobby checks");
                return 0;
            }
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
                    replay.Advance(
                        Enumerable
                            .Range(0, 8)
                            .Select(s => Input(tick, s))
                            .Select(frame => new MatchInput(frame))
                            .ToArray()
                    );
                }

                Console.WriteLine($"FROG_REPLAY_V1 ticks=2400 players=8 hash={replay.HashState():x16}");
                return 0;
            }

            if (args.Contains("--benchmark"))
            {
                BuildDiagnostics.Benchmark();
                return 0;
            }

            if (args.Contains("--tick-rate-study"))
            {
                TickRateStudy.Run();
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
            CompatibilityTests.Run();
            MatchArchitectureTests.Run();
            StocksAndCrewsTests.Run();
            BeachBallTests.Run();
            ModifierTests.Run();
            PhysicsFixTests.Run();
            SnapshotCoverageTests.Run();
            InterpolationSnapshotTests.Run();
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
            NetworkSessionTests.CheckpointContinuation();
            NetworkSessionTests.InputLatching();
            NetworkSessionTests.HighLatencyThroughput();
            NetworkSessionTests.InputWaitIndicator();
            NetworkSessionTests.OneRoundTripHandshake();
            NetworkSessionTests.InputWaitIndicator(hostSpectates: true);
            NetworkProtocolTests.PacketValidationAndDesync();
            NetworkProtocolTests.PacketStructure();
            LobbyRosterTests.Run();
            LobbyDiscoveryTests.Run(!args.Contains("--no-sockets"));
            SteamLegacyWireTests.Run();
            LobbySimulationTests.Run();
            CpuSimulationTests.Run();
            SpectatorNetworkTests.Run();
            MeshWireTests.Run(!args.Contains("--no-sockets"));
            LobbyTests.Run(Check);
            LobbyTests.RunLive();
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
