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
        Celebration();
        CrewBoutCelebration();
        SelectionControls();
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

    private static void Ready(World world, params int[] slots)
    {
        var inputs = new MatchInput[world.Players.Length];
        foreach (int slot in slots)
            inputs[slot] = new(default, new(MatchCommandKind.ToggleReady));
        world.Advance(inputs);
    }

    private static void SkipCelebration(World world, int slot)
    {
        var inputs = new MatchInput[world.Players.Length];
        inputs[slot] = new(default, new(MatchCommandKind.SkipCelebration));
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
        Check(world.Match.Phase == MatchPhase.Selecting, "Selecting both fighters still requires explicit readiness");
        Ready(world, 0, 2);
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
        Select(world, (1, 1), (3, 3));
        Ready(world, 0, 3);
        Check(
            world.Match.TeamSelections[0] == 0 && world.Match.Players[0].Stocks == 3 && !world.Match.IsShowdown,
            "The surviving fighter cannot be swapped out and keeps lives; a benched teammate prevents Showdown"
        );
        Kill(world, 0, 3);
        Select(world, (1, 1));
        Ready(world, 1, 3);
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
        Check(cpuRules.StartBlockedReason() == MatchStartBlock.CpuInCrews, "CPUs cannot enter Crews");
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
            {
                Select(world, (0, 0), (1, 1));
                Ready(world, 0, 1);
            }
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

    private static void SelectionControls()
    {
        var world = new World(
            TestFixtures.Map(),
            new GameRules(playerCount: 4, format: MatchFormat.Crews, scoring: ScoringMode.Stocks, teams: [0, 0, 1, 1])
        );
        Select(world, (0, 1));
        Check(world.Match.TeamSelections[0] == -1, "A teammate cannot sign another fighter up");
        Select(world, (0, 0), (1, 1));
        Check(world.Match.TeamSelections[0] == 0, "Simultaneous volunteers resolve in slot order");
        Ready(world, 1);
        Check(!world.Match.Players[0].Ready, "A teammate cannot ready the selected fighter");
        Ready(world, 0);
        Check(world.Match.Players[0].Ready, "The selected fighter can ready before the other team selects");
        byte[] ready = world.Capture();
        Ready(world, 0);
        Check(!world.Match.Players[0].Ready, "Ready toggles off on a second press");
        world.Restore(ready);
        Check(world.Match.Players[0].Ready, "Readiness survives snapshot restoration");
        world.Match.ApplyCommand(world.Rules, 1, new(MatchCommandKind.BackOutFighter));
        Check(world.Match.TeamSelections[0] == 0, "A teammate cannot back out the selected fighter");
        world.Match.ApplyCommand(world.Rules, 0, new(MatchCommandKind.BackOutFighter));
        Check(
            world.Match.TeamSelections[0] == -1 && !world.Match.Players[0].Ready,
            "Backing out clears the slot and readiness"
        );
        Select(world, (1, 1), (2, 2));
        Ready(world, 1, 2);
        Kill(world, 2, 5);
        SkipCelebration(world, 1);
        Check(
            world.Match.TeamSelections[0] == 1
                && !world.Match.Players[1].Ready
                && !world.Match.CanBackOut(world.Rules, 1),
            "Survivor stays locked but must ready again"
        );
        world.Match.ApplyCommand(world.Rules, 1, new(MatchCommandKind.BackOutFighter));
        Select(world, (0, 0));
        Check(world.Match.TeamSelections[0] == 1, "The survivor cannot back out or be replaced");
    }

    private static void Celebration()
    {
        foreach (var format in Enum.GetValues<MatchFormat>())
        foreach (var scoring in Enum.GetValues<ScoringMode>())
        {
            if (format == MatchFormat.Crews && scoring != ScoringMode.Stocks)
                continue;
            var rules = new GameRules(
                playerCount: 4,
                format: format,
                scoring: scoring,
                teams: [0, 0, 1, 1],
                startingStocks: 5,
                roundFinishTicks: 400,
                scoreScreenTicks: 200
            );
            var world = new World(TestFixtures.Map(), rules);
            if (format == MatchFormat.Crews)
            {
                Select(world, (1, 1), (3, 3));
                Ready(world, 1, 3);
                Kill(world, 3, 5);
                SkipCelebration(world, 1);
                Select(world, (2, 2));
                Ready(world, 1, 2);
                Kill(world, 2, 5);
                Check(
                    world.Match.Winner == 1 && world.Match.Phase == MatchPhase.RoundFinished,
                    "Crew victory celebrates the surviving fighter, not the first benched teammate"
                );
                Check(
                    world.Events.Any(e => e.Kind == SimulationEventKind.RoundWin && e.Player == 1),
                    "The final active frog receives the win VFX event"
                );
            }
            else
                world.Match.WinRound(rules, 1);
            int stocks = world.Match.Players[1].Stocks;
            int score = world.Match.Players[1].Score;
            Kill(world, 1, 99);
            Check(
                world.Match.Players[1].Stocks == stocks && world.Match.Players[1].Score == score,
                $"{format}/{scoring}: falling during a win never changes stocks or points"
            );
            Check(
                world.Events.Single(e => e.Kind == SimulationEventKind.Death).ScoreDelta == 0,
                $"{format}/{scoring}: celebration deaths never display a loss"
            );
            for (int tick = 0; tick <= World.TickRate; tick++)
                world.Advance(new MatchInput[4]);
            Check(world.Players[1].Alive, "The winner can respawn during the celebration");
            var input = new MatchInput[4];
            input[0] = new(default, new(MatchCommandKind.SkipCelebration));
            world.Advance(input);
            Check(world.Match.Phase == MatchPhase.RoundFinished, "Other players, including teammates, cannot skip");
            byte[] snapshot = world.Capture();
            input[0] = default;
            input[1] = new(default, new(MatchCommandKind.SkipCelebration));
            world.Advance(input);
            Check(world.Match.Phase == MatchPhase.RoundScores, "The winner can skip into the normal score sequence");
            byte[] skipped = world.Capture();
            world.Restore(snapshot);
            world.Advance(input);
            Check(world.Capture().SequenceEqual(skipped), "Victory skip is deterministic after rollback");
        }
    }

    private static void CrewBoutCelebration()
    {
        foreach (bool skip in new[] { false, true })
        {
            var rules = new GameRules(
                playerCount: 4,
                format: MatchFormat.Crews,
                scoring: ScoringMode.Stocks,
                teams: [0, 0, 1, 1],
                roundFinishTicks: 400,
                scoreScreenTicks: 200
            );
            var world = new World(TestFixtures.Map(), rules);
            Select(world, (1, 1), (3, 3));
            Ready(world, 1, 3);
            Kill(world, 3, 2);
            Check(
                world.Match.Phase == MatchPhase.Playing
                    && world.Events.All(e => e.Kind != SimulationEventKind.RoundWin),
                "Losing some crew lives does not end the bout"
            );
            Kill(world, 3, 3);
            Check(
                world.Match.Phase == MatchPhase.RoundFinished
                    && world.Match.Winner == 1
                    && world.Match.RoundNumber == 1
                    && world.Match.PhaseTicks == rules.RoundFinishTicks - 1,
                "An intermediate crew bout enters the full winner celebration before selection"
            );
            Check(
                world.Events.Any(e => e.Kind == SimulationEventKind.RoundWin && e.Player == 1),
                "Intermediate crew wins emit the same victory event as final wins"
            );
            Check(
                world.Match.Players.All(player => player.RoundWins == 0 && player.TotalScore == 0),
                "A bout celebration does not award the overall crew victory"
            );
            int lives = world.Match.Players[1].Stocks;
            Kill(world, 1, 99);
            Check(
                world.Match.Players[1].Stocks == lives
                    && world.Match.Players[1].Participation == Participation.Active
                    && world.Events.Single(e => e.Kind == SimulationEventKind.Death).ScoreDelta == 0,
                "The surviving fighter cannot lose carried-over lives during a bout celebration"
            );
            for (int tick = 0; tick <= World.TickRate; tick++)
                world.Advance(new MatchInput[4]);
            Check(world.Players[1].Alive, "The bout winner respawns for the celebration");
            SkipCelebration(world, 0);
            SkipCelebration(world, 3);
            Check(world.Match.Phase == MatchPhase.RoundFinished, "Benched allies and the loser cannot skip a bout win");
            byte[] before = world.Capture();
            void Finish()
            {
                if (skip)
                    SkipCelebration(world, 1);
                else
                    while (world.Match.Phase == MatchPhase.RoundFinished)
                        world.Advance(new MatchInput[4]);
            }
            Finish();
            Check(
                world.Match.Phase == MatchPhase.Selecting
                    && world.Match.RoundNumber == 2
                    && world.Match.Winner == -1
                    && world.Match.PhaseTicks == 0
                    && world.Match.TeamSelections[0] == 1
                    && world.Match.TeamSelections[1] == -1
                    && !world.Match.Players[1].Ready
                    && !world.Match.CanBackOut(rules, 1),
                $"A {(skip ? "skipped" : "completed")} bout celebration returns to selection with the survivor locked"
            );
            byte[] after = world.Capture();
            world.Restore(before);
            Finish();
            Check(world.Capture().SequenceEqual(after), "Bout celebration transitions survive snapshot rollback");
            Select(world, (2, 2));
            Ready(world, 1, 2);
            Check(
                world.Match.Phase == MatchPhase.Playing
                    && world.Match.Winner == -1
                    && world.Match.Players[1].Stocks == lives,
                "The next bout starts normally with the survivor's protected lives"
            );
        }
    }
}
