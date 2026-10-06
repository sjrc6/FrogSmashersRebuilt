using FrogSmashers.Core;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class BeachBallTests
{
    public static void Run()
    {
        Replay();
    }

    private static World Create() => new(TestFixtures.Map(), new GameRules(playerCount: 8, lobby: true));

    private static void Replay()
    {
        var world = Create();
        world.SetLobbySlot(0, true, 0);
        var replay = InputReplay.Start(world);
        for (int tick = 0; tick < 600; tick++)
        {
            var input = Enumerable.Range(0, 8).Select(slot => new MatchInput(TestFixtures.Input(tick, slot))).ToArray();
            world.Advance(input);
            replay.Record(input, world);
        }
        var restored = Create();
        replay.Play(restored);
        Check(
            world.HashState() == restored.HashState(),
            "Beach ball simulation and lobby attacks replay to the same hash"
        );
    }
}
