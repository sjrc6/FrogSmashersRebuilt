using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class SpectatorNetworkTests
{
    public static void Run()
    {
        Verify(false, false);
        Verify(true, false);
        Verify(false, true);
        Verify(false, false, 8);
    }

    private static void Verify(bool hostSpectates, bool absentObserver, int playerCount = 2)
    {
        int[][] slots = hostSpectates
            ?
            [
                [],
                [0],
                [1],
                [],
            ]
            :
            [
                [0],
                [1],
                [],
                [],
            ];
        if (playerCount == 8)
            slots = Enumerable.Range(0, 12).Select(peer => peer < 8 ? new[] { peer } : Array.Empty<int>()).ToArray();
        var wire = new SimulatedNetwork(slots.Length, 991, 3, 2, 8, 3);
        var sessions = Enumerable
            .Range(0, slots.Length)
            .Select(peer =>
            {
                var world = MakeWorld(playerCount);
                return new NetworkSession(
                    world,
                    new SessionConfig(slots, peer, "spectators", world),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        const int target = 750;
        int running = absentObserver ? slots.Length - 1 : slots.Length;
        for (int wall = 0; wall < 6000 && sessions.Take(running).Any(s => !s.AllPeersConfirmed(target - 1)); wall++)
        {
            wire.Advance();
            foreach (var session in sessions.Take(running))
            {
                session.Poll();
                Check(session.Error == null, session.Error ?? "Spectator network failure");
                if (session.World.TickNumber < target && !session.ShouldWaitForPeers)
                    session.TryAdvance(
                        session.LocalSlots.Select(slot => Input(session.World.TickNumber, slot)).ToArray()
                    );
            }
        }
        var baseline = MakeWorld(playerCount);
        for (int tick = 0; tick < target; tick++)
            baseline.Tick(
                Enumerable
                    .Range(0, playerCount)
                    .Select(slot => Input(Math.Max(0, tick - 2), slot))
                    .Select(input => tick < 2 ? default : input)
                    .ToArray()
            );
        foreach (var session in sessions.Take(running))
        {
            Check(
                session.World.TickNumber == target && session.AllPeersConfirmed(target - 1),
                "Players and spectators reach the final barrier without waiting for absent observers"
            );
            Check(
                session.World.HashState() == baseline.HashState(),
                "Spectator replay diverged from the player simulation"
            );
        }
        foreach (var session in sessions)
            session.Dispose();
        Console.WriteLine($"Spectators: host={hostSpectates}, absent={absentObserver}, all active simulations agree");
    }
}
