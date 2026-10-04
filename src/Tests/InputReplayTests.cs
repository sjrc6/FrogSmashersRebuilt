using FrogSmashers.Core;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class InputReplayTests
{
    public static void RoundTrip()
    {
        var replayWorld = CreateWorld();
        var replay = InputReplay.Start(replayWorld);
        for (int i = 0; i < 360; i++)
        {
            var input = replayWorld
                .Players.Select(p => new MatchInput(BotController.GetInput(replayWorld, p.Slot)))
                .ToArray();
            replayWorld.Advance(input);
            replay.Record(input, replayWorld);
        }

        ulong hash = replayWorld.HashState();
        var restored = CreateWorld();
        replay.Play(restored);
        Check(restored.HashState() == hash, "input replay restores its starting snapshot and verifies periodic hashes");
        string replayFile = Path.Combine(Path.GetTempPath(), $"frogsmashers-replay-{Guid.NewGuid():N}.fsr");
        try
        {
            replay.Save(replayFile);
            InputReplay.Load(replayFile).Play(restored);
            Check(restored.HashState() == hash, "replay binary format round-trips across disk");
        }
        finally
        {
            File.Delete(replayFile);
        }
    }
}
