using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class InterpolationSnapshotTests
{
    public static void Run()
    {
        Verify(TestFixtures.MakeWorld(8));
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true), new(1, Color: 1, Spawned: true)]);
        var lobby = new LobbySimulation(
            new World(TestFixtures.Map(), new GameRules(playerCount: 8, lobby: true)),
            roster
        );
        Verify(lobby.World, lobby);
    }

    private static void Verify(World world, LobbySimulation? lobby = null)
    {
        int[] inputRooms = lobby?.InputRooms.ToArray() ?? Enumerable.Range(0, world.Players.Length).ToArray();
        var wire = new SimulatedNetwork(2);
        var config = new SessionConfig(
            [Enumerable.Range(0, inputRooms.Length).ToArray()],
            0,
            "interpolation",
            world,
            1,
            rollback: new() { Delay = 0, MaxExtraDelay = 0 },
            inputPlayerSlots: inputRooms
        );
        using var session =
            lobby == null
                ? new NetworkSession(world, config, wire.Endpoint(0))
                : new NetworkSession(lobby, config, wire.Endpoint(0));
        for (int tick = 0; tick < 50; tick++)
        {
            byte[] expectedPrevious = world.Capture();
            Check(session.TryAdvance(new RollbackInput[inputRooms.Length]), "Local-owned session advances");
            Check(
                session.PreviousSnapshot.SequenceEqual(expectedPrevious),
                "Interpolation retains the state immediately before the tick"
            );
        }
    }
}
