using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class MatchArchitectureTests
{
    public static void Run()
    {
        Configuration();
        Progression();
        CommandsAndReplay();
        NetworkCommands();
        Console.WriteLine("Immutable configuration, persistent progression and selection replay/rollback passed");
    }

    private static void Configuration()
    {
        var rules = new GameRules(playerCount: 4, format: MatchFormat.Teams, teams: [0, 0, 1, 1], mapOrder: [0]);
        var world = new World(TestFixtures.Map(), rules, 42);
        var serialized = JsonSerializer.Serialize(rules);
        var copy = new World(TestFixtures.Map(), JsonSerializer.Deserialize<GameRules>(serialized)!, 42);
        Check(
            world.Capture().SequenceEqual(copy.Capture()),
            "JSON and local construction use the same validated rules"
        );
        Check(
            ReferenceEquals(world.Rules, rules),
            "World retains its immutable configuration without another rules copy"
        );
        Check(
            typeof(GameRules).GetProperties().All(property => !property.CanWrite),
            "Rule properties cannot be changed after creation"
        );
        Reject(() => ((IList<int>)rules.Teams)[0] = 7, "Rule collections cannot be mutated through an interface");
        Reject(() => new GameRules(playerCount: 9), "Invalid player count rejected");
        Reject(() => new GameRules(format: (MatchFormat)99), "Unknown format rejected");
        Reject(() => new GameRules(scoring: (ScoringMode)99), "Unknown scoring mode rejected");
        Reject(() => new GameRules(format: MatchFormat.Crews), "Crews cannot use Points");
        Reject(() => new GameRules(startingStocks: 0), "Stocks must be positive");
        Reject(
            () => JsonSerializer.Deserialize<GameRules>("{\"TeamMode\":true}"),
            "Obsolete TeamMode is not silently accepted"
        );
        Reject(
            () => new World(TestFixtures.Map(), new GameRules(mapOrder: [1])),
            "Map bounds are checked before simulation"
        );
        Check(
            new GameRules(
                playerCount: 4,
                format: MatchFormat.Crews,
                scoring: ScoringMode.Stocks,
                teams: [0, 1, 2, 0]
            ).StartBlockedReason() == "CREWS REQUIRES TWO TEAMS",
            "Crews requires exactly two teams at start"
        );
        Check(
            new GameRules(format: MatchFormat.Crews, scoring: ScoringMode.Stocks).StartBlockedReason()
                == "STOCKS NOT AVAILABLE",
            "Unimplemented modes cannot start with Points behavior"
        );
        var ffa = new World(TestFixtures.Map(), new GameRules(playerCount: 4, teams: rules.Teams, mapOrder: [0]), 42);
        Check(
            world.ConfigurationHash != ffa.ConfigurationHash,
            "Match format participates in compatibility and replay validation"
        );
    }

    private static void Progression()
    {
        var world = MechanicsFixture.CreateWorld(new GameRules(winScore: 99));
        var progress = world.Match.Players[0];
        progress.Score = 7;
        progress.RoundWins = 3;
        progress.Stocks = 2;
        var body = world.Players[0];
        body.X = Fixed.FromDecimal(world.Map.KillBounds.Right + 1);
        world.Advance(new MatchInput[2]);
        for (int tick = 0; tick < World.TickRate + 1; tick++)
            world.Advance(new MatchInput[2]);
        Check(
            !ReferenceEquals(body, world.Players[0]) && ReferenceEquals(progress, world.Match.Players[0]),
            "Respawn replaces only the frog body"
        );
        Check(
            progress.Score == 7 && progress.RoundWins == 3 && progress.Stocks == 2,
            "Respawn preserves all progression"
        );
        world.Match.Players[1].Participation = Participation.Waiting;
        world.Players[1].Alive = false;
        world.Players[1].SpawnTicks = 0;
        world.Advance(new MatchInput[2]);
        Check(!world.Players[1].Alive, "Waiting participants keep their identity without respawning");
        var saved = world.Capture();
        progress.Score = 100;
        world.Match.Players[1].Participation = Participation.Eliminated;
        world.Restore(saved);
        Check(
            world.Match.Players[0].Score == 7 && world.Match.Players[1].Participation == Participation.Waiting,
            "Snapshots restore progression independently of bodies"
        );
        world.Match.StartRound();
        Check(
            world.Match.Players[0].Score == 0
                && world.Match.Players[0].RoundWins == 3
                && world.Match.Players[0].Stocks == 2,
            "Starting an arena resets round points while preserving match progression"
        );
    }

    private static World SelectionWorld()
    {
        var world = new World(TestFixtures.Map(), new GameRules(playerCount: 4, format: MatchFormat.Teams), 123);
        world.Match.Phase = MatchPhase.Selecting;
        world.Match.Players[2].Participation = Participation.Waiting;
        world.Match.Players[3].Participation = Participation.Waiting;
        return world;
    }

    private static MatchInput Input(long tick, int slot) =>
        new(
            new InputFrame(1, 0, InputButtons.Jump),
            tick == 20 && slot < 2 ? new MatchCommand(MatchCommandKind.SelectFighter, (byte)(slot + 2)) : default
        );

    private static void CommandsAndReplay()
    {
        var world = SelectionWorld();
        var initialBody = world.Players[0].X;
        var replay = InputReplay.Start(world);
        for (int tick = 0; tick < 100; tick++)
        {
            var input = Enumerable.Range(0, 4).Select(slot => Input(tick, slot)).ToArray();
            world.Advance(input);
            replay.Record(input, world);
        }
        Check(
            world.TickNumber == 100 && world.Players[0].X == initialBody,
            "Selection advances the input clock while arena physics stays frozen"
        );
        Check(
            world.Match.TeamSelections[0] == 2 && world.Match.TeamSelections[1] == 3,
            "Each team can select its own waiting fighter"
        );
        world.Match.ApplySelection(world.Rules, 0, new(MatchCommandKind.SelectFighter, 1));
        Check(world.Match.TeamSelections[0] == 2, "Opposing team selection cannot be forged");
        var expected = world.Capture();
        var replayed = SelectionWorld();
        replay.Play(replayed);
        Check(replayed.Capture().SequenceEqual(expected), "Selection commands replay to the same state");
        string path = Path.Combine(Path.GetTempPath(), $"frog-selection-{Guid.NewGuid():N}.fsr");
        try
        {
            replay.Save(path);
            var loaded = InputReplay.Load(path);
            Check(loaded.Frames[20][0].Command.Player == 2, "Replay binary format retains selection commands");
            loaded.Play(replayed);
            Check(
                replayed.Capture().SequenceEqual(expected),
                "Selection replays verify checkpoints after a disk round trip"
            );
        }
        finally
        {
            File.Delete(path);
        }
        var codec = new RollbackInputCodec();
        var bytes = new byte[codec.Size];
        var selection = new RollbackInput(default, Match: new(MatchCommandKind.SelectFighter, 7));
        codec.Encode(selection, bytes);
        Check(codec.Decode(bytes) == selection, "Network codec retains match commands");
        bytes[^1] = 8;
        Reject(() => codec.Decode(bytes), "Network rejects out-of-range fighter selection");
        var adapter = new MatchSimulation(SelectionWorld(), [-1, 2, 0, 3, 1]);
        adapter.Tick([
            selection,
            default,
            new(default, Match: new(MatchCommandKind.SelectFighter, 2)),
            default,
            default,
        ]);
        Check(
            adapter.World.Match.TeamSelections[0] == 2 && adapter.World.Match.TeamSelections[1] == -1,
            "Selection authority follows the input-to-player mapping; host command streams cannot impersonate a fighter"
        );
    }

    private static void NetworkCommands()
    {
        var wire = new SimulatedNetwork(
            2,
            seed: 31,
            delayTicks: 7,
            jitterTicks: 4,
            lossPercent: 8,
            duplicatePercent: 4
        );
        int[][] slots =
        [
            [0, 2],
            [1, 3],
        ];
        var sessions = Enumerable
            .Range(0, 2)
            .Select(peer =>
            {
                var world = SelectionWorld();
                return new NetworkSession(
                    world,
                    new SessionConfig(
                        slots,
                        peer,
                        "selection",
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
            const int target = 120;
            for (int wall = 0; wall < 3000 && sessions.Any(session => session.ConfirmedFrame < target - 1); wall++)
            {
                wire.Advance();
                foreach (var session in sessions)
                {
                    session.Poll();
                    Check(session.Error == null, session.Error ?? "Selection transport failed");
                    if (session.World.TickNumber < target)
                        session.TryAdvance(
                            session
                                .LocalSlots.Select(slot =>
                                {
                                    var input = Input(session.World.TickNumber, slot);
                                    return new RollbackInput(input.Gameplay, Match: input.Command);
                                })
                                .ToArray()
                        );
                }
            }
            var offline = SelectionWorld();
            for (int tick = 0; tick < target; tick++)
                offline.Advance(Enumerable.Range(0, 4).Select(slot => Input(tick, slot)).ToArray());
            foreach (var session in sessions)
            {
                Check(
                    session.ConfirmedFrame == target - 1 && session.World.HashState() == offline.HashState(),
                    "Selection commands converge with offline playback under loss, jitter and duplication"
                );
                Check(session.RollbackCount > 0, "Selection network test exercises correction of predicted commands");
            }
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try
        {
            action();
        }
        catch (Exception error)
            when (error is ArgumentException or InvalidDataException or JsonException or NotSupportedException)
        {
            rejected = true;
        }
        Check(rejected, message);
    }
}
