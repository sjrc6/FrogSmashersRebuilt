using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class InterpolationSnapshotTests
{
    public static void Run()
    {
        Verify(new ObservedSimulation(TestFixtures.MakeWorld(8)), Enumerable.Range(0, 8).ToArray());
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true), new(1, Color: 1, Spawned: true)]);
        var lobby = new LobbySimulation(
            new World(TestFixtures.Map(), new GameRules { PlayerCount = 8, Lobby = true }),
            roster
        );
        Verify(new ObservedSimulation(lobby.World, lobby), lobby.InputRooms.ToArray());
    }

    private static void Verify(ObservedSimulation simulation, int[] inputRooms)
    {
        var wire = new SimulatedNetwork(2);
        using var session = new NetworkSession(
            simulation,
            new SessionConfig(
                [Enumerable.Range(0, inputRooms.Length).ToArray()],
                0,
                "interpolation",
                simulation.World,
                1,
                rollback: new() { Delay = 0, MaxExtraDelay = 0 },
                inputPlayerSlots: inputRooms
            ),
            wire.Endpoint(0)
        );
        for (int tick = 0; tick < 50; tick++)
        {
            byte[] expectedPrevious = simulation.World.Capture();
            Check(session.TryAdvance(new RollbackInput[inputRooms.Length]), "local-owned session advances");
            Check(
                session.PreviousSnapshot.SequenceEqual(expectedPrevious),
                "interpolation retains the state immediately before the tick"
            );
            Check(
                ReferenceEquals(session.PreviousSnapshot, simulation.Snapshots[tick]),
                "interpolation reuses the world bytes saved for rollback"
            );
            Check(simulation.Snapshots.Count == tick + 2, "one initial snapshot and one save per simulated tick");
        }
    }

    private sealed class ObservedSimulation(World world, LobbySimulation? lobby = null) : IRollbackSimulation
    {
        public World World => world;
        public Dictionary<long, byte[]> Snapshots { get; } = new();

        public byte[] Capture() => lobby?.Capture() ?? World.Capture();

        public byte[] Capture(out byte[] worldSnapshot)
        {
            byte[] state;
            if (lobby != null)
                state = lobby.Capture(out worldSnapshot);
            else
                state = worldSnapshot = World.Capture();
            Snapshots[World.TickNumber] = worldSnapshot;
            return state;
        }

        public void Restore(byte[] state) => Restore(state, out _);

        public void Restore(byte[] state, out byte[] worldSnapshot)
        {
            if (lobby != null)
                lobby.Restore(state, out worldSnapshot);
            else
            {
                World.Restore(state);
                worldSnapshot = state;
            }
        }

        public void Tick(ReadOnlySpan<RollbackInput> inputs)
        {
            if (lobby != null)
                lobby.Tick(inputs);
            else
                World.Tick(inputs.ToArray().Select(input => input.Gameplay).ToArray());
        }
    }
}
