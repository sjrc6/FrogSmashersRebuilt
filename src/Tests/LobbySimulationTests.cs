using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class LobbySimulationTests
{
    public static void Run()
    {
        CompactInputsAddressTheirOwnRooms();
        CommandsAndColorRandomnessRestoreExactly();
        RosterChangesPreserveIncumbentFrogs();
        CpuRetaliationSurvivesRollback();
        EmptyLobbyAndSnapshotsRemainIndependent();
        ColorSelectionRequiresTheStartingPlatform();
        SpectatingRetainsTheLastConfirmedSelection();
        SlotPoliciesAreOutsideSimulation();
        TeamsRespectCapacityAndRollback();
    }

    private static void SlotPoliciesAreOutsideSimulation()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true)]);
        roster.Edit(1, SlotType.Cpu);
        var original = Create(roster);
        roster.Edit(2, SlotType.Closed);
        roster.Edit(3, SlotType.Friend);
        roster.Edit(4, SlotType.Private);
        roster.Edit(5, SlotType.Local);
        var changed = Create(roster);
        Check(
            original.Capture().SequenceEqual(changed.Capture()),
            "Access rules cannot enter rollback snapshots or hashes"
        );
        original.Tick([default, default]);
        changed.Tick([default, default]);
        Check(
            original.Capture().SequenceEqual(changed.Capture()),
            "Access rules cannot affect deterministic lobby gameplay"
        );
        roster.ApplyMembership(original.Membership);
        Check(
            roster.Slots[2].Type == SlotType.Closed
                && roster.Slots[3].Type == SlotType.Friend
                && roster.Slots[4].Type == SlotType.Private
                && roster.Slots[5].Type == SlotType.Local,
            "Restoring membership preserves access rules"
        );
    }

    private static LobbySimulation Create(LobbyRoster roster, uint seed = 71) =>
        new(new World(TestFixtures.Map(), new GameRules { Lobby = true, PlayerCount = 8 }, 13), roster, seed);

    private static void TeamsRespectCapacityAndRollback()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, Enumerable.Range(0, 5).Select(id => new LobbyPlayer(id, Team: 1, Color: id)).ToArray());
        Check(roster.Players(0).Count(player => player.Team == 1) == 4, "A fifth admission cannot overfill a team");
        Check(roster.Players(0)[4].Team == 2, "Admission chooses the next available team color");
        var simulation = Create(roster);
        var inputs = new RollbackInput[6];
        inputs[5] = new(default, TeamStep: -1);
        simulation.Tick(inputs);
        Check(simulation.Membership.Rooms[4]!.Team == 0, "Backward color cycling skips a full team");
        inputs[5] = new(default, TeamStep: 1);
        simulation.Tick(inputs);
        Check(simulation.Membership.Rooms[4]!.Team == 2, "Forward color cycling skips a full team");

        byte[] snapshot = simulation.Capture();
        for (int frame = 0; frame < 100; frame++)
        {
            for (int handle = 1; handle < inputs.Length; handle++)
                inputs[handle] = new(default, TeamStep: (sbyte)(handle % 2 == 0 ? 1 : -1));
            simulation.Tick(inputs);
            Check(
                simulation
                    .Membership.Rooms.OfType<LobbyPlayer>()
                    .GroupBy(player => player.Team)
                    .All(team => team.Count() <= 4),
                "Simultaneous color changes respect the team limit"
            );
        }
        byte[] result = simulation.Capture();
        simulation.Restore(snapshot);
        for (int frame = 0; frame < 100; frame++)
            simulation.Tick(inputs);
        Check(simulation.Capture().SequenceEqual(result), "Team selection replays deterministically after rollback");

        var incumbents = new LobbyRoster();
        incumbents.SetPlayers(
            0,
            Enumerable.Range(0, 4).Select(id => new LobbyPlayer(id, Team: 1, Color: id)).ToArray()
        );
        simulation = Create(incumbents);
        var stale = new LobbyRoster();
        stale.SetPlayers(0, Enumerable.Range(0, 4).Select(id => new LobbyPlayer(id, Team: 0, Color: id)).ToArray());
        stale.SetPlayers(1, [new(0, Team: 1, Color: 4)]);
        simulation.ApplyRoster(stale);
        Check(
            simulation.Membership.Humans(0).All(player => player.Team == 1),
            "Admission preserves current team selections"
        );
        Check(
            simulation.Membership.Humans(1)[0].Team == 2,
            "Admission resolves capacity against current simulation teams"
        );

        var cpuRoster = new LobbyRoster();
        cpuRoster.SetPlayers(0, Enumerable.Range(0, 4).Select(id => new LobbyPlayer(id, Team: 4, Color: id)).ToArray());
        cpuRoster.Edit(4, SlotType.Cpu);
        Check(cpuRoster.Slots[4].Player is { Cpu: true, Team: 5 }, "CPU creation also skips full teams");
        var command = LobbyRosterCommand.From(1, cpuRoster.Membership());
        Check(command.IsValid, "Valid team composition fits the roster command");
        ulong teamBits = 7ul << (4 * 8 + 3);
        command = command with { Details = (command.Details & ~teamBits) | (4ul << (4 * 8 + 3)) };
        Check(!command.IsValid, "Roster commands reject a fifth teammate");
    }

    private static void CompactInputsAddressTheirOwnRooms()
    {
        var roster = new LobbyRoster();
        var slots = roster.Slots.ToArray();
        slots[2] = new(SlotType.Open, new LobbyPlayer(0, Color: 2));
        slots[7] = new(SlotType.Open, new LobbyPlayer(1, Peer: 1, Color: 7));
        roster.Replace(slots);
        var simulation = Create(roster);
        Check(
            simulation.InputRooms.SequenceEqual(new[] { -1, 2, 7 }),
            "Lobby input handles compact occupied rooms in room order"
        );
        simulation.Tick([default, default, new(default, (byte)LobbyInputActions.Spawn, TeamStep: 1)]);
        Check(!simulation.Membership.Rooms[2]!.Spawned, "A remote spawn command cannot affect another handle's room");
        Check(
            simulation.Membership.Rooms[7] is { Spawned: true, Team: 1 },
            "Unspawned players retain deterministic input handles"
        );
        Check(simulation.World.Players[7].Alive, "A spawn command creates the frog on its mapped room platform");
        simulation.Tick([default, new(default, ColorStep: 1), default]);
        Check(simulation.Membership.Rooms[7]!.Color == 7, "Changing one frog's color leaves other room colors alone");
    }

    private static void CommandsAndColorRandomnessRestoreExactly()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0), new(1, Color: 1)]);
        var simulation = Create(roster);
        byte[] checkpoint = simulation.Capture();
        var states = new List<byte[]>();
        var previewEvents = new List<SimulationEvent[]>();
        for (int tick = 0; tick < 80; tick++)
        {
            simulation.Tick(ColorInputs(tick));
            states.Add(simulation.Capture());
            previewEvents.Add(simulation.Events.ToArray());
            Check(
                simulation.Membership.Rooms.OfType<LobbyPlayer>().Select(player => player.Color).Distinct().Count()
                    == 2,
                "Simultaneous color commands cannot assign duplicate colors"
            );
        }
        simulation.Restore(checkpoint);
        for (int tick = 0; tick < states.Count; tick++)
        {
            simulation.Tick(ColorInputs(tick));
            Check(
                simulation.Capture().AsSpan().SequenceEqual(states[tick]),
                "Rollback restores lobby cosmetics, world state, and the color RNG"
            );
            Check(
                simulation.Events.SequenceEqual(previewEvents[tick]),
                "Rollback regenerates identical color-preview events"
            );
        }
    }

    private static RollbackInput[] ColorInputs(int tick) =>
        [
            default,
            new(
                default,
                ColorStep: tick % 3 == 0 ? (sbyte)1 : (sbyte)0,
                TeamStep: tick % 7 == 0 ? (sbyte)-1 : (sbyte)0
            ),
            new(
                default,
                ColorStep: tick % 4 == 0 ? (sbyte)-1 : (sbyte)0,
                TeamStep: tick % 5 == 0 ? (sbyte)1 : (sbyte)0
            ),
        ];

    private static void RosterChangesPreserveIncumbentFrogs()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Color: 3, Spawned: true)]);
        var simulation = Create(roster);
        simulation.Tick([default, default]);
        simulation.World.Players[0].X = 9;
        simulation.World.Players[0].Y = 14;
        simulation.World.Players[0].VX = 7;
        simulation.World.Players[0].Score = 6;
        var requested = new LobbyRoster();
        requested.SetPlayers(0, [new(0, Color: 0, Spawned: false)]);
        requested.SetPlayers(1, [new(1, Peer: 1, Color: 3)]);
        simulation.ApplyRoster(requested);
        Check(
            simulation.Membership.Rooms[0] is { Spawned: true, Color: 3 },
            "Membership proposals cannot overwrite incumbent cosmetic or spawn state"
        );
        Check(
            simulation.Membership.Rooms[1]!.Color != 3,
            "New admissions resolve collisions with incumbent colors deterministically"
        );
        Check(
            simulation.World.Players[0].Alive
                && simulation.World.Players[0].X == 9
                && simulation.World.Players[0].Y == 14
                && simulation.World.Players[0].VX == 7
                && simulation.World.Players[0].Score == 6,
            "Admitting a new connection preserves an existing frog's physical state"
        );

        var moved = new LobbyRoster();
        var slots = moved.Slots.ToArray();
        slots[6] = new(SlotType.Open, new(0, Color: 0));
        moved.Replace(slots);
        simulation.ApplyRoster(moved);
        Check(
            simulation.World.Players[6].Alive
                && simulation.World.Players[6].X == 9
                && simulation.World.Players[6].Slot == 6,
            "Room reassignment preserves frog identity and position"
        );
        Check(simulation.World.Players[0].Eliminated, "The previous room no longer owns a moved frog");
        byte[] snapshot = simulation.Capture();
        simulation.Restore(snapshot);
        Check(simulation.World.Players[6].Score == 6, "Moved frogs retain valid standalone snapshots");
    }

    private static void CpuRetaliationSurvivesRollback()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true)]);
        roster.Edit(1, SlotType.Cpu);
        roster.SetPlayers(0, [.. roster.Players(0), new(1, Color: 2, Spawned: true)]);
        var simulation = Create(roster);
        simulation.Tick([default, default, default]);
        Check(simulation.World.Players[1].PreviousInput == default, "Lobby CPUs remain peaceful until hit");
        simulation.World.Players[0].X = simulation.World.Players[1].X - 4;
        simulation.World.Players[0].Y = simulation.World.Players[1].Y;
        simulation.World.Players[0].Facing = 1;
        for (int room = 0; room < 3; room++)
            simulation.World.Players[room].SpawnTicks = 0;
        bool hit = false;
        for (int tick = 0; tick < 20 && !hit; tick++)
        {
            simulation.Tick([default, new(tick < 3 ? new(0, 0, InputButtons.Attack) : default), default]);
            hit = simulation.Events.Any(item =>
                item.Kind == SimulationEventKind.Hit && item.Player == 1 && item.Other == 0
            );
        }
        Check(hit, "Lobby CPU rollback fixture delivers a real attack");
        simulation.World.Players[0].X = simulation.World.Players[1].X - 10;
        simulation.World.Players[0].Y = simulation.World.Players[1].Y;
        simulation.World.Players[2].X = simulation.World.Players[1].X + 1;
        simulation.World.Players[2].Y = simulation.World.Players[1].Y;
        byte[] checkpoint = simulation.Capture();
        simulation.Tick([default, default, default]);
        Check(
            simulation.World.Players[1].PreviousInput.X == -1,
            "Provoked CPU follows its attacker instead of a closer innocent player"
        );
        byte[] expected = simulation.Capture();
        simulation.Restore(checkpoint);
        simulation.Tick([default, default, default]);
        Check(
            simulation.Capture().AsSpan().SequenceEqual(expected),
            "CPU retaliation survives restored PlayerState instances"
        );
        simulation.World.SetLobbySlot(0, false, 0);
        simulation.World.SetLobbySlot(0, true, 0);
        simulation.Tick([default, default, default]);
        Check(
            simulation.World.Players[1].PreviousInput == default,
            "Replacing the attacker clears retaliation through stable spawn generations"
        );
    }

    private static void EmptyLobbyAndSnapshotsRemainIndependent()
    {
        var empty = Create(new LobbyRoster());
        empty.Tick([default]);
        empty.Tick([default]);
        Check(
            empty.World.TickNumber == 2 && empty.World.Players.All(player => player.Eliminated),
            "Empty lobbies support a permanent host command stream"
        );
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0)]);
        var simulation = Create(roster);
        byte[] snapshot = simulation.Capture();
        roster.Reset();
        Check(simulation.Membership.Count == 1, "Simulation roster does not alias its admission proposal");
        simulation.Restore(snapshot);
        byte[] expected = simulation.Capture();
        Array.Fill(snapshot, (byte)0);
        Check(
            simulation.Capture().AsSpan().SequenceEqual(expected),
            "Restored lobby state does not alias snapshot bytes"
        );
    }

    private static void ColorSelectionRequiresTheStartingPlatform()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true)]);
        var map = TestFixtures.Map();
        map.Spawns = Enumerable.Range(0, 8).Select(room => new PointData { X = room * 4 - 14, Y = 0 }).ToList();
        map.Collision[0].X = map.Spawns[0].X;
        var simulation = new LobbySimulation(new World(map, new GameRules { Lobby = true, PlayerCount = 8 }), roster);
        for (int tick = 0; tick < 20; tick++)
            simulation.Tick([default, default]);
        Check(
            LobbySimulation.OnStartingPlatform(simulation.World, 0),
            "Lobby selection fixture lands on its starting platform"
        );
        simulation.World.Players[0].OnGround = false;
        simulation.Tick([default, new(default, (byte)LobbyInputActions.SelectColor)]);
        Check(simulation.Membership.Rooms[0]!.Spawned, "A selection command cannot despawn an airborne frog");
        for (int tick = 0; tick < 20; tick++)
            simulation.Tick([default, default]);
        simulation.Tick([default, new(default, (byte)LobbyInputActions.SelectColor)]);
        Check(
            !simulation.Membership.Rooms[0]!.Spawned && simulation.World.Players[0].Eliminated,
            "Landing on the starting platform enables deterministic color selection"
        );
        Check(
            simulation.Events.Any(item => item.Kind == SimulationEventKind.LobbyPreview),
            "Returning to color selection creates a reproducible preview event"
        );
        simulation.Tick([default, new(default, (byte)LobbyInputActions.Spawn)]);
        Check(
            simulation.Membership.Rooms[0]!.Spawned && simulation.World.Players[0].Alive,
            "The spawn command restores the selected frog"
        );
    }

    private static void SpectatingRetainsTheLastConfirmedSelection()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0)]);
        var simulation = Create(roster);
        roster.SetSpectating(0, true);
        simulation.Tick([default, new(default, ColorStep: 1, TeamStep: 1)]);
        var selected = simulation.Membership.Rooms[0]!;
        simulation.ApplyRoster(roster);
        Check(
            simulation.Membership.Spectator(0) is { } spectator
                && spectator.Color == selected.Color
                && spectator.Team == selected.Team,
            "A spectating transaction preserves selections confirmed while draining the old session"
        );
    }
}
