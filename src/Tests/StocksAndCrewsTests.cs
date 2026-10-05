using FrogSmashers.Core;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class StocksAndCrewsTests
{
    public static void Run()
    {
        Stocks();
        TeamStocks();
        Crews();
        SlotOrder();
        Console.WriteLine("Stocks, human crews, combo life loss, selection and slot-order wins passed");
    }

    private static void Kill(World world, int slot, int hits = 1)
    {
        var player = world.Players[slot];
        player.Alive = true;
        player.X = Fixed.FromDecimal(world.Map.KillBounds.Right + 10);
        player.HitsTaken = hits;
        player.LastHitBy = -1;
        world.Advance(new MatchInput[world.Players.Length]);
    }

    private static void Stocks()
    {
        var rules = new GameRules(
            scoring: ScoringMode.Stocks,
            startingStocks: 5,
            matchRounds: 2,
            roundFinishTicks: 0,
            scoreScreenTicks: 0
        );
        var world = new World(TestFixtures.Map(), rules);
        Kill(world, 0, 3);
        Check(
            world.Match.Players[0].Stocks == 2 && world.Match.Phase == MatchPhase.Playing,
            "A three-hit KO removes three lives, even without attacker credit"
        );
        Check(
            world.Events.Single(e => e.Kind == SimulationEventKind.Death).ScoreDelta == -3,
            "HUD receives actual life loss"
        );
        for (int tick = 0; tick < World.TickRate + 1; tick++)
            world.Advance(new MatchInput[2]);
        Check(world.Players[0].Alive, "Remaining lives permit respawn");
        Kill(world, 0, 0);
        Check(world.Match.Players[0].Stocks == 1, "An unhit fall costs one life");
        Kill(world, 0, 10);
        Check(
            world.Events.Single(e => e.Kind == SimulationEventKind.Death).ScoreDelta == -1,
            "Overkill never reports more lives than remain"
        );
        Check(
            world.Match.RoundNumber == 2 && world.Match.Players.All(p => p.Stocks == 5),
            "Next Stocks round replenishes every fighter"
        );
        Kill(world, 1, 5);
        Check(
            world.Match.IsShowdown && world.Match.Players.All(p => p.Stocks == 1),
            "Tied standings enter one-life Showdown"
        );
        Kill(world, 0, 4);
        Check(
            world.Match.Phase == MatchPhase.MatchFinished && world.Match.Winner == 1,
            "Showdown resolves the tied match"
        );
    }

    private static void TeamStocks()
    {
        var world = new World(
            TestFixtures.Map(),
            new GameRules(
                playerCount: 4,
                format: MatchFormat.Teams,
                scoring: ScoringMode.Stocks,
                startingStocks: 2,
                teams: [0, 0, 1, 1]
            )
        );
        Kill(world, 0, 2);
        Check(
            world.Match.Players[0].Participation == Participation.Eliminated
                && world.Match.Players[1].Stocks == 2
                && world.Match.Winner == -1,
            "Teammates own separate lives"
        );
        for (int tick = 0; tick < 200; tick++)
            world.Advance(new MatchInput[4]);
        Check(!world.Players[0].Alive, "Eliminated fighters cannot respawn");
        Kill(world, 1, 2);
        Check(
            world.Match.Winner == 2 && world.Match.Players[2].RoundWins == 1 && world.Match.Players[3].RoundWins == 1,
            "The last surviving team receives one round win"
        );
    }

    private static void Select(World world, params (int Source, int Target)[] choices)
    {
        var inputs = new MatchInput[world.Players.Length];
        foreach (var (source, target) in choices)
            inputs[source] = new(default, new(MatchCommandKind.SelectFighter, (byte)target));
        world.Advance(inputs);
    }

    private static void Crews()
    {
        var rules = new GameRules(
            playerCount: 4,
            format: MatchFormat.Crews,
            scoring: ScoringMode.Stocks,
            teams: [0, 0, 1, 1],
            startingStocks: 5,
            roundFinishTicks: 0,
            scoreScreenTicks: 0
        );
        var arena = TestFixtures.Map();
        var showdown = new MapData
        {
            Id = "final",
            Role = MapRole.Showdown,
            Spawns = arena.Spawns,
            Collision = arena.Collision,
        };
        var world = new World([arena, showdown], rules, 71, null);
        Check(
            world.Match.Phase == MatchPhase.Selecting
                && world.Match.Players.All(p => p.Participation == Participation.Waiting),
            "Crews starts with both teams choosing on the frozen transition screen"
        );
        Select(world, (0, 2));
        Check(world.Match.TeamSelections[0] == -1, "A teammate cannot choose for the opponent");
        Select(world, (0, 0));
        byte[] frozen = world.Capture();
        for (int tick = 0; tick < 30; tick++)
            world.Advance(new MatchInput[4]);
        Check(world.Players.All(p => !p.Alive), "Physics and spawning stay frozen while only one side is ready");
        world.Restore(frozen);
        Select(world, (2, 2));
        Check(
            world.Match.Phase == MatchPhase.Playing
                && world.Match.Players.Count(p => p.Participation == Participation.Active) == 2,
            "Exactly one fighter per crew enters the arena"
        );
        Kill(world, 0, 2);
        Check(
            world.Match.Players[0].Stocks == 3 && world.Match.Phase == MatchPhase.Playing,
            "A nonfinal KO respawns the same crew fighter"
        );
        Kill(world, 2, 5);
        Check(
            world.Match.Phase == MatchPhase.Selecting && world.Match.TeamSelections[0] == 0,
            "The survivor is locked in for the next bout"
        );
        Select(world, (1, 1), (2, 3));
        Check(
            world.Match.TeamSelections[0] == 0 && world.Match.Players[0].Stocks == 3 && !world.Match.IsShowdown,
            "The surviving fighter cannot be swapped out and keeps lives; a benched teammate prevents Showdown"
        );
        Kill(world, 0, 3);
        Select(world, (0, 1));
        Check(
            world.Match.IsShowdown
                && world.Map.Id == "final"
                && world.Match.Players[1].Stocks == 5
                && world.Match.Players[3].Stocks == 5,
            "Only the last two remaining fighters enter Showdown with their lives intact"
        );
        Kill(world, 3, 3);
        Check(
            world.Match.Phase == MatchPhase.Playing && world.Match.Players[3].Stocks == 2,
            "Crew Showdown still uses combo-based lives"
        );
        Kill(world, 3, 2);
        Check(
            world.Match.Phase == MatchPhase.MatchFinished && world.Match.Winner == 1,
            "Crew victory ends the entire match"
        );
        var cpuRules = new GameRules(
            format: MatchFormat.Crews,
            scoring: ScoringMode.Stocks,
            cpuPlayers: [true, false, false, false, false, false, false, false]
        );
        Check(cpuRules.StartBlockedReason() == "CREWS REQUIRES HUMAN PLAYERS", "CPUs cannot enter Crews");
    }

    private static void SlotOrder()
    {
        foreach (var format in Enum.GetValues<MatchFormat>())
        foreach (var scoring in Enum.GetValues<ScoringMode>())
        {
            if (format == MatchFormat.Crews && scoring == ScoringMode.Points)
                continue;
            var world = new World(
                TestFixtures.Map(),
                new GameRules(format: format, scoring: scoring, winScore: 1, startingStocks: 1)
            );
            if (format == MatchFormat.Crews)
                Select(world, (0, 0), (1, 1));
            foreach (var player in world.Players)
            {
                player.Alive = true;
                player.X = Fixed.FromDecimal(world.Map.KillBounds.Right + 10);
                player.LastHitBy = 1 - player.Slot;
            }
            world.Advance(new MatchInput[2]);
            Check(world.Match.Winner == 1, $"{format}/{scoring} resolves simultaneous KOs in slot order");
        }
    }
}
