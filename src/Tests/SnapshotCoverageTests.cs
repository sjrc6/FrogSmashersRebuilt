using System.Reflection;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class SnapshotCoverageTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly HashSet<string> WorldConfiguration =
    [
        "maps",
        "collisionMaps",
        "tuning",
        "events",
        "snapshotBuffer",
        "<Rules>k__BackingField",
        "<ConfigurationHash>k__BackingField",
    ];

    public static void Run()
    {
        for (int sample = 1; sample <= 2; sample++)
        {
            var map = TestFixtures.Map();
            var rules = new GameRules(playerCount: 8, teams: [.. Enumerable.Range(0, 8).ToArray()]);
            var source = new World([map, map], rules, 19, null);
            var target = new World([map, map], rules, 71, null);
            foreach (var player in source.Players)
            {
                int slot = player.Slot;
                Populate(player, sample + slot);
                player.Slot = slot;
                player.Team = rules.Teams[slot];
                player.ColorIndex = (slot + sample) % 8;
                player.Facing = sample == 1 ? -1 : 1;
            }
            Populate(source.Fly, sample);
            Populate(source.BeachBall, sample);
            source.Fly.Owner = sample;
            source.Fly.IngestedBy = sample + 1;
            foreach (var field in typeof(World).GetFields(Fields))
            {
                if (
                    WorldConfiguration.Contains(field.Name)
                    || field.FieldType == typeof(PlayerState[])
                    || field.FieldType == typeof(MatchState)
                    || field.FieldType == typeof(FlyState)
                    || field.FieldType == typeof(BeachBallState)
                )
                    continue;
                field.SetValue(source, Sample(field.FieldType, sample));
            }
            PopulateMatch(source.Match, rules, sample);
            target.Restore(source.Capture());
            CompareWorld(source, target);
            byte[] isolated = source.Capture();
            source.Players[0].X += 1;
            byte[] updated = source.Capture();
            target.Restore(isolated);
            Check(
                target.Players[0].X != source.Players[0].X,
                "Snapshot storage is isolated from live state and later captures"
            );
            target.Restore(updated);
            CompareWorld(source, target);
            source.Restore(isolated);
            Check(source.Capture().SequenceEqual(isolated), "Recapturing restored history preserves its exact bytes");
        }

        var bots = new LobbyBots();
        foreach (var field in typeof(LobbyBots).GetFields(Fields))
        {
            var values =
                field.GetValue(bots) as Array
                ?? throw new Exception($"Add a snapshot sample for LobbyBots.{field.Name}");
            for (int index = 0; index < values.Length; index++)
                values.SetValue(
                    field.FieldType == typeof(bool[]) ? (object)(index % 2 == 0) : (uint)(index + 37),
                    index
                );
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        bots.Write(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        CompareFields(bots, LobbyBots.Read(reader));
        foreach (int sample in new[] { 1, 2 })
        {
            var player = new LobbyPlayer(0);
            Populate(player, sample);
            stream.Position = 0;
            player.WriteSnapshot(writer);
            stream.Position = 0;
            CompareFields(player, LobbyPlayer.ReadSnapshot(reader));
        }
        LobbyState();
    }

    private static void LobbyState()
    {
        var rules = new GameRules(playerCount: 8, lobby: true);
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Color: 3, Spawned: true), new(1, Color: 5)]);
        var source = new LobbySimulation(new World(TestFixtures.Map(), rules, 1), roster, 123);
        var target = new LobbySimulation(new World(TestFixtures.Map(), rules, 9), new LobbyRoster(), 456);
        foreach (var field in typeof(LobbySimulation).GetFields(Fields))
        {
            if (field.Name is "<CpuRevision>k__BackingField" or "<RosterRevision>k__BackingField")
                field.SetValue(source, 123);
            else if (field.FieldType == typeof(uint))
                field.SetValue(source, field.Name == "pendingPreviews" ? 0x5au : 1234567u);
            else
                Check(
                    new[]
                    {
                        "bots",
                        "events",
                        "inputRooms",
                        "inputSources",
                        "<World>k__BackingField",
                        "<Membership>k__BackingField",
                    }.Contains(field.Name),
                    $"Classify new lobby state field {field.Name} for snapshot coverage"
                );
        }
        target.Restore(source.Capture());
        foreach (var field in typeof(LobbySimulation).GetFields(Fields).Where(field => field.FieldType == typeof(uint)))
            Check(Equals(field.GetValue(source), field.GetValue(target)), $"Lobby snapshot omitted {field.Name}");
        Check(
            source.Membership.Rooms.SequenceEqual(target.Membership.Rooms),
            "Lobby snapshot restores actual slot records"
        );
        Check(source.RosterRevision == target.RosterRevision, "Lobby snapshot restores membership revision");
        Check(source.CpuRevision == target.CpuRevision, "Lobby snapshot restores applied CPU command revision");
        Check(source.InputSources.SequenceEqual(target.InputSources), "Lobby snapshot rebuilds input identities");
        Check(source.InputRooms.SequenceEqual(target.InputRooms), "Lobby snapshot rebuilds input rooms");
        CompareWorld(source.World, target.World);
    }

    private static void PopulateMatch(MatchState match, GameRules rules, int sample)
    {
        foreach (var player in match.Players)
            Populate(player, sample);
        foreach (var field in typeof(MatchState).GetFields(Fields))
        {
            if (field.FieldType == typeof(PlayerProgress[]))
                continue;
            if (field.FieldType == typeof(int[]))
            {
                for (int team = 0; team < 8; team++)
                    match.TeamSelections[team] = Array.FindIndex(rules.Teams.ToArray(), value => value == team);
                continue;
            }
            field.SetValue(
                match,
                field.Name switch
                {
                    nameof(MatchState.CurrentMapIndex) => 1,
                    nameof(MatchState.Winner) => 3,
                    _ => Sample(field.FieldType, sample),
                }
            );
        }
    }

    private static void Populate(object value, int sample)
    {
        foreach (var field in value.GetType().GetFields(Fields))
            field.SetValue(value, Sample(field.FieldType, sample));
    }

    private static object Sample(Type type, int sample)
    {
        if (type == typeof(bool))
            return sample % 2 != 0;
        if (type == typeof(int))
            return 71 + sample;
        if (type == typeof(long))
            return 19003L + sample;
        if (type == typeof(uint))
            return 103731u + (uint)sample;
        if (type == typeof(Fixed))
            return Fixed.FromDecimal(sample * 7.125m);
        if (type == typeof(string))
            return "snapshot-field-" + sample;
        if (type == typeof(InputFrame))
            return new InputFrame(1, -1, InputButtons.Attack | InputButtons.Strafe);
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(1)!;
        throw new Exception($"Add a nondefault snapshot sample for {type.Name}");
    }

    private static void CompareWorld(World expected, World actual)
    {
        foreach (var field in typeof(World).GetFields(Fields))
        {
            if (WorldConfiguration.Contains(field.Name))
                continue;
            if (field.FieldType == typeof(PlayerState[]))
                for (int index = 0; index < expected.Players.Length; index++)
                    CompareFields(expected.Players[index], actual.Players[index]);
            else if (field.FieldType == typeof(MatchState))
            {
                foreach (var matchField in typeof(MatchState).GetFields(Fields))
                {
                    if (matchField.FieldType == typeof(PlayerProgress[]))
                        for (int index = 0; index < expected.Match.Players.Length; index++)
                            CompareFields(expected.Match.Players[index], actual.Match.Players[index]);
                    else if (matchField.FieldType == typeof(int[]))
                        Check(
                            ((int[])matchField.GetValue(expected.Match)!).SequenceEqual(
                                (int[])matchField.GetValue(actual.Match)!
                            ),
                            $"Match snapshot omitted {matchField.Name}"
                        );
                    else
                        Check(
                            Equals(matchField.GetValue(expected.Match), matchField.GetValue(actual.Match)),
                            $"Match snapshot omitted {matchField.Name}"
                        );
                }
            }
            else if (field.FieldType == typeof(BeachBallState))
                CompareFields(expected.BeachBall, actual.BeachBall);
            else if (field.FieldType == typeof(FlyState))
                CompareFields(expected.Fly, actual.Fly);
            else
                Check(Equals(field.GetValue(expected), field.GetValue(actual)), $"World snapshot omitted {field.Name}");
        }
    }

    private static void CompareFields(object expected, object actual)
    {
        foreach (var field in expected.GetType().GetFields(Fields))
        {
            object? a = field.GetValue(expected),
                b = field.GetValue(actual);
            bool equal = a is Array array
                ? b is Array other && array.Cast<object>().SequenceEqual(other.Cast<object>())
                : Equals(a, b);
            Check(equal, $"Snapshot omitted or changed {expected.GetType().Name}.{field.Name}");
        }
    }
}
