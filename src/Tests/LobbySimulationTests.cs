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
    }

    private static LobbySimulation Create(LobbyRoster roster, uint seed = 71) =>
        new(new World(TestFixtures.Map(), new GameRules { Lobby = true, PlayerCount = 8 }, 13), roster, seed);

    private static void CompactInputsAddressTheirOwnRooms()
    {
        var roster = new LobbyRoster();
        var slots = roster.Slots.ToArray();
        slots[2] = new(SlotType.Open, new LobbyPlayer(0, Color: 2));
        slots[7] = new(SlotType.Open, new LobbyPlayer(1, Peer: 1, Color: 7));
        roster.Replace(slots);
        var simulation = Create(roster);
        Check(
            simulation.InputRooms.SequenceEqual(new[] { 2, 7 }),
            "Lobby input handles compact occupied rooms in room order"
        );
        simulation.Tick([default, new(default, (byte)LobbyInputActions.Spawn, TeamStep: 1)]);
        Check(
            !simulation.Roster.Slots[2].Player!.Spawned,
            "A remote spawn command cannot affect another handle's room"
        );
        Check(
            simulation.Roster.Slots[7].Player is { Spawned: true, Team: 1 },
            "Unspawned players retain deterministic input handles"
        );
        Check(simulation.World.Players[7].Alive, "A spawn command creates the frog on its mapped room platform");
        simulation.Tick([new(default, ColorStep: 1), default]);
        Check(
            simulation.Roster.Slots[7].Player!.Color == 7,
            "Changing one frog's color leaves other room colors alone"
        );
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
                simulation
                    .Roster.Slots.Where(slot => slot.Player != null)
                    .Select(slot => slot.Player!.Color)
                    .Distinct()
                    .Count() == 2,
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
        simulation.Tick([default]);
        simulation.World.Players[0].X = 9;
        simulation.World.Players[0].Y = 14;
        simulation.World.Players[0].VX = 7;
        simulation.World.Players[0].Score = 6;
        var requested = new LobbyRoster();
        requested.SetPlayers(0, [new(0, Color: 0, Spawned: false)]);
        requested.SetPlayers(1, [new(1, Peer: 1, Color: 3)]);
        simulation.ApplyRoster(requested);
        Check(
            simulation.Roster.Slots[0].Player is { Spawned: true, Color: 3 },
            "Membership proposals cannot overwrite incumbent cosmetic or spawn state"
        );
        Check(
            simulation.Roster.Slots[1].Player!.Color != 3,
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
            simulation.Tick([new(tick < 3 ? new(0, 0, InputButtons.Attack) : default), default, default]);
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
        empty.Tick([]);
        empty.Tick([default]);
        Check(
            empty.World.TickNumber == 2 && empty.World.Players.All(player => player.Eliminated),
            "Empty lobbies support a neutral placeholder stream"
        );
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0)]);
        var simulation = Create(roster);
        byte[] snapshot = simulation.Capture();
        roster.Reset();
        Check(simulation.Roster.Count == 1, "Simulation roster does not alias its admission proposal");
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
            simulation.Tick([default]);
        Check(
            LobbySimulation.OnStartingPlatform(simulation.World, 0),
            "Lobby selection fixture lands on its starting platform"
        );
        simulation.World.Players[0].OnGround = false;
        simulation.Tick([new(default, (byte)LobbyInputActions.SelectColor)]);
        Check(simulation.Roster.Slots[0].Player!.Spawned, "A selection command cannot despawn an airborne frog");
        for (int tick = 0; tick < 20; tick++)
            simulation.Tick([default]);
        simulation.Tick([new(default, (byte)LobbyInputActions.SelectColor)]);
        Check(
            !simulation.Roster.Slots[0].Player!.Spawned && simulation.World.Players[0].Eliminated,
            "Landing on the starting platform enables deterministic color selection"
        );
        Check(
            simulation.Events.Any(item => item.Kind == SimulationEventKind.LobbyPreview),
            "Returning to color selection creates a reproducible preview event"
        );
        simulation.Tick([new(default, (byte)LobbyInputActions.Spawn)]);
        Check(
            simulation.Roster.Slots[0].Player!.Spawned && simulation.World.Players[0].Alive,
            "The spawn command restores the selected frog"
        );
    }

    private static void SpectatingRetainsTheLastConfirmedSelection()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0)]);
        var simulation = Create(roster);
        roster.SetSpectating(0, true);
        simulation.Tick([new(default, ColorStep: 1, TeamStep: 1)]);
        var selected = simulation.Roster.Slots[0].Player!;
        simulation.ApplyRoster(roster);
        Check(
            simulation.Roster.Spectator(0) is { } spectator
                && spectator.Color == selected.Color
                && spectator.Team == selected.Team,
            "A spectating transaction preserves selections confirmed while draining the old session"
        );
    }
}
