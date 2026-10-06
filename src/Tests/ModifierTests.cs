using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class ModifierTests
{
    public static void Run()
    {
        Configuration();
        Scores();
        BodyBouncing();
        Flies();
        Rollback();
        Console.WriteLine("Modifiers: configuration, scoring, body bouncing, fly timing and lossy rollback passed");
    }

    private static void Configuration()
    {
        var defaults = GameModifiers.Default;
        Check(
            defaults.PhysicsFixes && !defaults.BodyBouncing && defaults.FlyEnabled,
            "Physics fixes and flies default on; body bouncing requires opt-in"
        );
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
        var hashes = new HashSet<ulong> { CreateWorld().ConfigurationHash };
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
                    FlySpawnMinSeconds = 0,
                },
                defaults with
                {
                    FlySpawnMaxSeconds = 121,
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

    private static void BodyBouncing()
    {
        World Contact(GameModifiers modifiers, bool apex = false, bool teams = false)
        {
            var world = CreateWorld(
                new(playerCount: 3, format: teams ? MatchFormat.Teams : MatchFormat.Ffa, modifiers: modifiers)
            );
            var bouncer = world.Players[0];
            bouncer.X = 0;
            bouncer.Y = 5;
            bouncer.OnGround = false;
            bouncer.Mode = CharacterMode.Bouncing;
            bouncer.HitsTaken = 2;
            bouncer.LastHitBy = 2;
            bouncer.VX = 20;
            bouncer.VY = -1;
            bouncer.HasReachedApex = apex;
            world.Players[1].X = 1;
            world.Players[1].Y = 5;
            return world;
        }
        var enabled = new GameModifiers { BodyBouncing = true };
        foreach (bool teams in new[] { false, true })
        foreach (bool redirect in new[] { false, true })
        {
            var world = Contact(enabled with { RedirectBounces = redirect }, teams: teams);
            Step(world);
            Check(
                world.Players[1].HitsTaken == 1
                    && world.Players[1].LastHitBy == 2
                    && world.Players[1].Mode == CharacterMode.Bouncing
                    && world.Players[1].VX == 15,
                "Body hit transfers three-quarter velocity, a combo hit and the original attacker's credit"
            );
            Check(
                (world.Players[0].VX < 0) == redirect && world.Events.Any(e => e.HitKind == HitKind.Body),
                "Redirect option controls the launched frog's outgoing trajectory"
            );
        }
        foreach (var world in new[] { Contact(GameModifiers.Default), Contact(enabled, apex: true) })
        {
            Step(world);
            Check(world.Players[1].HitsTaken == 0, "Body collisions obey opt-in and apex restrictions");
        }
        var afterApex = Contact(enabled with { BounceBeforeRecoveryOnly = false }, apex: true);
        Step(afterApex);
        Check(afterApex.Players[1].HitsTaken == 1, "Recovery setting permits body hits after the launch apex");
        var immune = Contact(enabled);
        immune.Players[0].HasBounceDodged = true;
        Step(immune);
        Check(immune.Players[1].HitsTaken == 0, "A bounce dodge disables body hits");
    }

    private static void Flies()
    {
        var world = CreateWorld(new(modifiers: new() { FlySpawnMinSeconds = 1, FlySpawnMaxSeconds = 1 }));
        Step(world, count: World.TickRate - 1);
        Check(!world.Fly.Active, "Fixed fly delay does not spawn early");
        Step(world);
        Check(
            world.Fly.Active && world.Events.Any(e => e.Kind == SimulationEventKind.FlySpawn),
            "Equal delay limits spawn at the configured tick"
        );
        world.Fly.X = 100;
        Step(world);
        Check(
            !world.Fly.Active && world.Fly.SpawnTicks == World.TickRate,
            "A lost fly uses the configured respawn delay"
        );
        foreach (
            var rules in new[] { new GameRules(modifiers: new() { FlyEnabled = false }), new GameRules(showdown: true) }
        )
        {
            var disabled = CreateWorld(rules);
            disabled.Fly.SpawnTicks = 1;
            Step(disabled, count: 3);
            Check(!disabled.Fly.Active, "Disabled flies and Showdown never spawn a fly");
        }
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
