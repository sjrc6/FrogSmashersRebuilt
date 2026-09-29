using FrogSmashers.Core;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class ReplayTests
{
    public static void ReplayAndSnapshots()
    {
        var a = MakeWorld(8);
        var b = MakeWorld(8);
        var checkpoints = new Dictionary<int, byte[]>();
        for (int tick = 0; tick < 2400; tick++)
        {
            if (tick % 79 == 0)
            {
                checkpoints[tick] = a.Capture();
            }

            var input = Enumerable.Range(0, 8).Select(s => Input(tick, s)).ToArray();
            a.Tick(input);
            b.Tick(input);
            Check(a.HashState() == b.HashState(), $"Identical replay diverged at {tick}");
        }

        var expected = a.Capture();
        foreach (var pair in checkpoints)
        {
            b.Restore(pair.Value);
            for (long tick = b.TickNumber; tick < 2400; tick++)
            {
                b.Tick(Enumerable.Range(0, 8).Select(s => Input(tick, s)).ToArray());
            }

            Check(expected.SequenceEqual(b.Capture()), $"Restore/resimulation changed state at {pair.Key}");
        }

        var c = new World(Map(), new GameRules { PlayerCount = 8, WinScore = 3 }, 12345);
        bool refused = false;
        try
        {
            c.Restore(expected);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Check(refused, "Snapshot restored into incompatible rules");
        Console.WriteLine(
            $"Replay+snapshot roundtrip: 8 players, 2400 ticks, {checkpoints.Count} rewind points, hash {a.HashState():x16}"
        );
    }
}
