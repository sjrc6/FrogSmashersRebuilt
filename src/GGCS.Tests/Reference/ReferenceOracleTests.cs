using System.Text.Json;
using GGCS.Core;

namespace GGCS.Tests;

internal static class ReferenceOracleTests
{
    public static void Run(Action<bool, string> assert)
    {
        using var stream =
            typeof(ReferenceOracleTests).Assembly.GetManifestResourceStream("GGCS.Tests.Reference.ggrs-traces.json")
            ?? throw new InvalidOperationException("The GGRS reference trace is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var queue = new DelayedInputQueue<byte>(0, 10);
        foreach (var command in root.GetProperty("delayCommands").EnumerateArray())
        {
            if (command.GetProperty("operation").GetString() == "delay")
            {
                queue.SetDelay(command.GetProperty("value").GetInt32());
                continue;
            }

            int frame = command.GetProperty("frame").GetInt32();
            var output = queue.Submit(frame, command.GetProperty("input").GetByte());
            var expected = command.GetProperty("outputs");
            assert(output.Count == expected.GetArrayLength(), $"GGRS delay trace frame {frame}: output count");
            for (int index = 0; index < output.Count; index++)
            {
                assert(
                    output[index].Frame == expected[index][0].GetInt32(),
                    $"GGRS delay trace frame {frame}: delayed frame {index}"
                );
                assert(
                    output[index].Input == expected[index][1].GetByte(),
                    $"GGRS delay trace frame {frame}: input {index}"
                );
            }
        }

        var timeSync = new TimeSync();
        foreach (var sample in root.GetProperty("timeSamples").EnumerateArray())
        {
            int frame = sample[0].GetInt32();
            timeSync.Record(frame, sample[1].GetInt32(), sample[2].GetInt32());
            assert(
                timeSync.AverageFrameAdvantage == sample[3].GetInt32(),
                $"GGRS time synchronization trace frame {frame}"
            );
        }

        var game = new TraceGame();
        var engine = new RollbackEngine<byte, ulong>(1, new SessionOptions(), game);
        foreach (var command in root.GetProperty("predictionCommands").EnumerateArray())
        {
            game.Advances.Clear();
            string operation = command.GetProperty("operation").GetString()!;
            switch (operation)
            {
                case "add":
                    engine.AddInput(
                        0,
                        command.GetProperty("inputFrame").GetInt32(),
                        command.GetProperty("input").GetByte()
                    );
                    break;
                case "advance":
                    assert(engine.Advance(), "GGRS prediction trace must advance within its prediction window");
                    break;
                case "repair":
                    engine.Repair();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown GGRS trace operation: {operation}");
            }

            int frame = command.GetProperty("frame").GetInt32();
            assert(engine.CurrentFrame == frame, $"GGRS prediction trace {operation}: frame");
            assert(
                game.State == command.GetProperty("state").GetUInt64(),
                $"GGRS prediction trace {operation} at {frame}: state"
            );
            var advances = command.GetProperty("advances");
            assert(
                game.Advances.Count == advances.GetArrayLength(),
                $"GGRS prediction trace {operation} at {frame}: advances"
            );
            for (int index = 0; index < game.Advances.Count; index++)
            {
                var actual = game.Advances[index];
                assert(actual.Frame == advances[index][0].GetInt32(), $"GGRS prediction trace advance {index}: frame");
                assert(actual.Input == advances[index][1].GetByte(), $"GGRS prediction trace advance {index}: input");
                assert(
                    (int)actual.Status == advances[index][2].GetInt32(),
                    $"GGRS prediction trace advance {index}: status"
                );
            }
        }
    }

    private sealed class TraceGame : IRollbackGame<byte, ulong>
    {
        public ulong State;
        public List<(int Frame, byte Input, InputStatus Status)> Advances { get; } = new();

        public SavedState<ulong> SaveState() => new(State, State);

        public void LoadState(ulong state) => State = state;

        public void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<byte>> inputs, bool isResimulation)
        {
            State = unchecked(State * 31 + inputs[0].Input);
            Advances.Add((frame, inputs[0].Input, inputs[0].Status));
        }
    }
}
