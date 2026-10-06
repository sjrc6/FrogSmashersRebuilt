using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class ModifierTests
{
    public static void Run()
    {
        Configuration();
        Scores();
        Rollback();
        Console.WriteLine("Modifiers: configuration, scoring and lossy rollback passed");
    }

    private static void Configuration()
    {
        var defaults = new GameModifiers
        {
            PhysicsFixes = true,
            BodyBouncing = false,
            BounceBeforeRecoveryOnly = true,
            RedirectBounces = false,
            SuicidePenalty = false,
            MatchScoring = MatchScoring.RoundWins,
            FlyEnabled = true,
            FlySpawnMinSeconds = 15,
            FlySpawnMaxSeconds = 45,
        };
        GameModifiers[] variants =
        [
            defaults with
            {
                PhysicsFixes = false,
            },
            defaults with
            {
                BodyBouncing = true,
            },
            defaults with
            {
                BounceBeforeRecoveryOnly = false,
            },
            defaults with
            {
                RedirectBounces = true,
            },
            defaults with
            {
                SuicidePenalty = true,
            },
            defaults with
            {
                MatchScoring = MatchScoring.CumulativePoints,
            },
            defaults with
            {
                FlyEnabled = false,
            },
            defaults with
            {
                FlySpawnMinSeconds = 1,
            },
            defaults with
            {
                FlySpawnMaxSeconds = 120,
            },
        ];
        var hashes = new HashSet<ulong> { CreateWorld(new(modifiers: defaults)).ConfigurationHash };
        foreach (var modifiers in variants)
        {
            var rules = new GameRules(modifiers: modifiers);
            var copy = JsonSerializer.Deserialize<GameRules>(JsonSerializer.Serialize(rules))!;
            Check(
                copy.Modifiers == modifiers && hashes.Add(CreateWorld(copy).ConfigurationHash),
                "Each modifier round-trips and changes the snapshot/replay configuration identity"
            );
        }
        foreach (
            var invalid in new[]
            {
                defaults with
                {
                    FlySpawnMinSeconds = GameModifiers.MinimumFlyDelay - 1,
                },
                defaults with
                {
                    FlySpawnMaxSeconds = GameModifiers.MaximumFlyDelay + 1,
                },
                defaults with
                {
                    FlySpawnMinSeconds = 46,
                },
                defaults with
                {
                    MatchScoring = (MatchScoring)99,
                },
            }
        )
        {
            bool rejected = false;
            try
            {
                _ = new GameRules(modifiers: invalid);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Check(rejected, "Invalid modifier ranges and enum values are rejected before simulation");
        }
        MapData[] maps =
        [
            new()
            {
                Id = "tie",
                Name = "RENAMED",
                Role = MapRole.Showdown,
            },
            new() { Id = "a", Name = "Showdown is just a display name" },
            new() { Id = "presentation", Role = MapRole.Presentation },
            new() { Id = "b" },
        ];
        Check(
            ArenaRotation.StartingAt(maps, 3).SequenceEqual(new[] { 3, 1 }),
            "Regular rotation uses roles, independent of map index or display name"
        );
        Check(
            new World(maps, new(showdown: true), 1, null).Map.Id == "tie",
            "Showdown is selected by its explicit role"
        );
    }

    private static void Scores()
    {
        var modifiers = new GameModifiers { SuicidePenalty = true, MatchScoring = MatchScoring.CumulativePoints };
        var rules = new GameRules(
            playerCount: 4,
            format: MatchFormat.Teams,
            teams: [0, 0, 1, 1],
            winScore: 10,
            matchRounds: 2,
            modifiers: modifiers
        );
        var match = new MatchState(rules);
        match.RecordDeath(rules, 0, -1, 0);
        Check(
            match.Players[0].Score == -1 && match.Players[1].Score == -1 && match.Players[2].Score == 0,
            "Suicide penalty subtracts once from the whole team and permits negative scores"
        );
        match.RecordDeath(rules, 0, 2, 5);
        match.RecordDeath(rules, 2, 0, 11);
        match.WinRound(rules, 0);
        Check(
            match.Players.Select(p => p.TotalScore).SequenceEqual(new[] { 10, 10, 5, 5 }),
            "Cumulative totals count each team's round score once for both winners and losers"
        );
        match.AdvanceRound(rules);
        match.StartRound(rules);
        match.RecordDeath(rules, 2, 0, 1);
        match.RecordDeath(rules, 0, 2, 10);
        match.WinRound(rules, 2);
        Check(
            !match.AdvanceRound(rules)
                && match.Winner == 2
                && match.Players[2].TotalScore == 15
                && match.Players[0].RoundWins == 1
                && match.Players[2].RoundWins == 1,
            "Cumulative points resolve the match independently of tied round wins"
        );

        var world = CreateWorld(new(modifiers: modifiers));
        world.Players[0].X = 51;
        Step(world);
        Check(
            world.Match.Players[0].Score == -1
                && world.Events.Single(e => e.Kind == SimulationEventKind.Death).ScoreDelta == -1,
            "A suicide emits its actual negative score change for presentation"
        );
        var saved = world.Capture();
        world.Restore(saved);
        Check(world.Match.Players[0].Score == -1, "Negative scores survive snapshots");
        var stocks = new GameRules(scoring: ScoringMode.Stocks, modifiers: modifiers);
        Check(
            !stocks.CumulativeScoring && new MatchState(stocks).DeathScore(stocks, -1, 0) == -1,
            "Points-only modifiers have no effect on Stocks"
        );
        var tieRules = new GameRules(matchRounds: 1, modifiers: modifiers);
        var tie = new MatchState(tieRules);
        tie.Players[0].Score = tie.Players[1].Score = 4;
        tie.WinRound(tieRules, 0);
        Check(
            tie.AdvanceRound(tieRules)
                && tie.IsShowdown
                && tie.Players.All(p => p.Participation == Participation.Active),
            "Cumulative ties advance to Showdown"
        );
    }

    private static void Rollback()
    {
        foreach (bool fixes in new[] { false, true })
        {
            var rules = new GameRules(
                winScore: 999,
                modifiers: new()
                {
                    PhysicsFixes = fixes,
                    BodyBouncing = true,
                    RedirectBounces = true,
                    BounceBeforeRecoveryOnly = false,
                    SuicidePenalty = true,
                    MatchScoring = MatchScoring.CumulativePoints,
                    FlySpawnMinSeconds = 1,
                    FlySpawnMaxSeconds = 2,
                }
            );
            World Make() => new(TestFixtures.Map(), rules, 381);
            var wire = new SimulatedNetwork(
                2,
                seed: 17,
                delayTicks: 5,
                jitterTicks: 3,
                lossPercent: 8,
                duplicatePercent: 4
            );
            int[][] slots =
            [
                [0],
                [1],
            ];
            var sessions = Enumerable
                .Range(0, 2)
                .Select(peer =>
                {
                    var world = Make();
                    return new NetworkSession(
                        world,
                        new SessionConfig(
                            slots,
                            peer,
                            "modifiers",
                            world,
                            1,
                            rollback: new RollbackPreferences { Delay = 0 }
                        ),
                        wire.Endpoint(peer)
                    );
                })
                .ToArray();
            try
            {
                const int target = 600;
                for (int wall = 0; wall < 6000 && sessions.Any(s => s.ConfirmedFrame < target - 1); wall++)
                {
                    wire.Advance();
                    foreach (var session in sessions)
                    {
                        session.Poll();
                        Check(session.Error == null, session.Error ?? "Modifier transport failed");
                        if (session.World.TickNumber < target)
                            session.TryAdvance(
                                session
                                    .LocalSlots.Select(slot => new RollbackInput(
                                        TestFixtures.Input(session.World.TickNumber, slot)
                                    ))
                                    .ToArray()
                            );
                    }
                }
                var offline = Make();
                var recorder = InputReplay.Start(offline);
                for (int tick = 0; tick < target; tick++)
                {
                    var inputs = Enumerable
                        .Range(0, 2)
                        .Select(slot => new MatchInput(TestFixtures.Input(tick, slot)))
                        .ToArray();
                    offline.Advance(inputs);
                    recorder.Record(inputs, offline);
                }
                foreach (var session in sessions)
                    Check(
                        session.ConfirmedFrame == target - 1
                            && session.World.HashState() == offline.HashState()
                            && session.RollbackCount > 0,
                        "Both physics choices converge with offline play after lossy rollback"
                    );
                string path = Path.Combine(Path.GetTempPath(), "frog-modifiers-" + Guid.NewGuid() + ".replay");
                try
                {
                    recorder.Save(path);
                    var replay = InputReplay.Load(path);
                    var playback = Make();
                    replay.Play(playback);
                    Check(
                        playback.HashState() == offline.HashState(),
                        "Modifier matches replay identically from their saved snapshot"
                    );
                }
                finally
                {
                    File.Delete(path);
                }
            }
            finally
            {
                foreach (var session in sessions)
                    session.Dispose();
            }
        }
    }
}
