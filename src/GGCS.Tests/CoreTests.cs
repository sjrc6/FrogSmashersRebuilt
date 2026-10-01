using GGCS.Core;

namespace GGCS.Tests;

internal static class CoreTests
{
    public static void Run()
    {
        DelayedInputsFillAndDropWithoutGaps();
        DelayedInputsRejectInvalidSubmissions();
        DelayedInputsContinueFromCheckpoint();
        PredictionStopsAndRepairsAtTheBoundary();
        MatchingPredictionsNeedNoReplay();
        PredictionUsesAgeAndClearsEdges();
        ZeroPredictionWaitsForActualInputs();
        HistoryIsIndependentOfPrediction();
        DisconnectsRepairInputAndStatus();
        FinalizationProtectsConsumedHistory();
        PlayerQueuesRejectMalformedInput();
        SnapshotsUseFrameBoundaries();
        MutableSnapshotsRemainIsolated();
        TimeSyncMatchesReference();
        LongRunningHistoryWrapsSafely();
        RandomizedDelayChangesMatchAnUnboundedModel();
        RandomizedArrivalAndPredictionMatchTheConfirmedTimeline();
        RandomizedDisconnectCorrectionsMatchTheFinalCutoff();
    }

    private static SessionOptions Options(int prediction = 4, int history = 16) =>
        new()
        {
            MaxPredictionFrames = prediction,
            HistoryFrames = history,
            MaxInputDelay = 4,
            SpectatorBufferFrames = 0,
        };

    private static void DelayedInputsFillAndDropWithoutGaps()
    {
        var queue = new DelayedInputQueue<int>(2, 8);
        AssertInputs(queue.Submit(0, 9), (0, 0), (1, 0), (2, 9));
        AssertInputs(queue.Submit(1, 6), (3, 6));
        queue.SetDelay(4);
        AssertInputs(queue.Submit(2, 8), (4, 6), (5, 6), (6, 8));
        queue.SetDelay(1);
        for (int frame = 3; frame < 6; frame++)
            Check.Equal(0, queue.Submit(frame, 100).Count, "Reducing delay drops overlapping frames");
        AssertInputs(queue.Submit(6, 4), (7, 4));
        queue.SetDelay(3);
        queue.SetDelay(2);
        AssertInputs(queue.Submit(7, 3), (8, 4), (9, 3));
    }

    private static void DelayedInputsRejectInvalidSubmissions()
    {
        Check.Throws<ArgumentOutOfRangeException>(() => new DelayedInputQueue<int>(0, -1), "Negative max delay");
        Check.Throws<ArgumentOutOfRangeException>(() => new DelayedInputQueue<int>(2, 1), "Delay exceeds maximum");
        var queue = new DelayedInputQueue<int>(0, 2);
        Check.Throws<ArgumentException>(() => queue.Submit(1, 0), "First submission starts at zero");
        AssertInputs(queue.Submit(0, 1), (0, 1));
        Check.Throws<ArgumentException>(() => queue.Submit(0, 1), "Duplicate submission rejected");
        Check.Throws<ArgumentException>(() => queue.Submit(2, 1), "Skipped submission rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => queue.SetDelay(-1), "Negative delay rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => queue.SetDelay(3), "Excess delay rejected");
        AssertInputs(queue.Submit(1, 2), (1, 2));
    }

    private static void DelayedInputsContinueFromCheckpoint()
    {
        var queue = new DelayedInputQueue<int>(2, 8);
        queue.Seed([4, 7]);
        AssertInputs(queue.Submit(0, 9), (2, 9));
        AssertInputs(queue.Submit(1, 11), (3, 11));
        queue.SetDelay(4);
        AssertInputs(queue.Submit(2, 13), (4, 11), (5, 11), (6, 13));
        Check.Throws<InvalidOperationException>(
            () => queue.Seed([1, 2, 3, 4]),
            "Cannot replace an active delay prefix"
        );
        var empty = new DelayedInputQueue<int>(2, 8);
        Check.Throws<InvalidOperationException>(() => empty.Seed([1]), "Seed must fill the delay exactly");
        empty.Seed([2, 6]);
        empty.SetDelay(4);
        AssertInputs(empty.Submit(0, 8), (2, 6), (3, 6), (4, 8));
    }

    private static void PredictionStopsAndRepairsAtTheBoundary()
    {
        var game = new TestGame();
        var engine = new RollbackEngine<int, TestState>(1, Options(), game);
        for (int frame = 0; frame < 4; frame++)
            Check.True(engine.Advance(), "Each allowed speculative frame advances");
        Check.True(!engine.Advance(), "Prediction threshold stops advancement");
        Check.Equal(4, engine.CurrentFrame, "Prediction limit has no off-by-one error");
        Check.True(
            engine.TryGetChecksum(0, out ulong initial) && initial == 0,
            "Initial state remains available at prediction limit"
        );

        engine.AddInput(0, 0, 3);
        Check.Equal(4, engine.Repair(), "Earliest speculative frame can still be repaired");
        Check.Equal(
            Expected((3, false), (3, false), (3, false), (3, false)),
            game.Value,
            "Repair resimulates from actual input"
        );
        Check.Equal(4, game.Resimulations, "Replayed callbacks marked as resimulation");
        Check.Equal(0, engine.Repair(), "A completed repair is not repeated");
        Check.True(engine.Advance(), "Receiving one input releases one prediction frame");
        Check.True(!engine.Advance(), "Prediction stalls again at the adjusted boundary");
    }

    private static void MatchingPredictionsNeedNoReplay()
    {
        var game = new TestGame();
        var engine = new RollbackEngine<int, TestState>(3, Options(), game);
        for (int player = 0; player < 3; player++)
            engine.AddInput(player, 0, player + 1);
        Check.True(engine.Advance(), "Multiple players have independent input queues");
        Check.True(engine.Advance(), "All three previous inputs are predicted");
        for (int player = 0; player < 3; player++)
            engine.AddInput(player, 1, player + 1);
        Check.Equal(0, engine.Repair(), "Matching actual values do not cause unnecessary rollback");
        Check.True(engine.TryGetInputs(1, out var inputs), "Actual input available once every player arrives");
        for (int player = 0; player < 3; player++)
        {
            Check.Equal(player + 1, inputs[player].Input, "Player input order preserved");
            Check.Equal(InputStatus.Confirmed, inputs[player].Status, "Confirmed stream excludes predicted status");
        }
        inputs[0] = default;
        Check.True(
            engine.TryGetInputs(1, out var again) && again[0].Input == 1,
            "Returned arrays cannot corrupt retained inputs"
        );
    }

    private static void PredictionUsesAgeAndClearsEdges()
    {
        var game = new TestGame();
        var ages = new List<int>();
        int Predict(int input, int age)
        {
            ages.Add(age);
            return age == 1 ? input & 0x7f : 0;
        }
        var engine = new RollbackEngine<int, TestState>(1, Options(), game, Predict);
        engine.AddInput(0, 0, 0x85);
        Check.True(engine.Advance(), "Actual edge-bearing input advances");
        Check.True(engine.Advance(), "First prediction clears edge flags");
        Check.True(engine.Advance(), "Later prediction can decay held input");
        Check.Equal(1, ages[0], "Prediction starts at age one");
        Check.Equal(2, ages[1], "Prediction receives elapsed missing-frame age");
        engine.AddInput(0, 1, 5);
        Check.Equal(0, engine.Repair(), "Comparison uses recorded prediction rather than raw previous input");
        engine.AddInput(0, 2, 2);
        Check.Equal(1, engine.Repair(), "Different later input repairs just its suffix");
        Check.Equal(Expected((0x85, false), (5, false), (2, false)), game.Value, "Custom prediction repairs correctly");
    }

    private static void ZeroPredictionWaitsForActualInputs()
    {
        var game = new TestGame();
        var engine = new RollbackEngine<int, TestState>(2, Options(prediction: 0), game);
        Check.True(!engine.Advance(), "Lockstep waits when all inputs are absent");
        engine.AddInput(0, 0, 5);
        Check.True(!engine.Advance(), "Lockstep waits for the final player");
        engine.AddInput(1, 0, 8);
        Check.True(engine.Advance(), "Lockstep advances with every actual input");
        Check.True(!engine.Advance(), "Lockstep does not predict the next frame");
        Check.Equal(0, game.Resimulations, "Lockstep performs no speculative replay");
    }

    private static void HistoryIsIndependentOfPrediction()
    {
        var game = new TestGame();
        var engine = new RollbackEngine<int, TestState>(1, Options(prediction: 2, history: 8), game);
        for (int frame = 0; frame < 8; frame++)
        {
            engine.AddInput(0, frame, frame + 1);
            Check.True(engine.Advance(), "Available actual inputs are not constrained by prediction budget");
        }
        engine.AddInput(0, 8, 9);
        Check.True(!engine.Advance(), "Unfinalized history cannot be overwritten");
        engine.SetConfirmedFrame(0);
        Check.True(engine.Advance(), "Finalizing one frame frees one history entry");
        Check.True(!engine.TryGetInputs(0, out _), "Retired input is unavailable");
        Check.True(engine.TryGetInputs(1, out _), "Older input remains independent of prediction budget");
        Check.True(engine.TryGetState(1, out _), "Oldest retained snapshot exists after wraparound");
        engine.DisconnectPlayer(0, 1);
        Check.Equal(7, engine.Repair(), "Disconnect can repair beyond prediction depth within retained history");
        Check.Equal(
            Expected(
                (1, false),
                (2, false),
                (0, true),
                (0, true),
                (0, true),
                (0, true),
                (0, true),
                (0, true),
                (0, true)
            ),
            game.Value,
            "Extended-history disconnect correction converges"
        );
    }

    private static void DisconnectsRepairInputAndStatus()
    {
        var game = new TestGame();
        var engine = new RollbackEngine<int, TestState>(1, Options(), game);
        for (int frame = 0; frame < 4; frame++)
        {
            engine.AddInput(0, frame, 0);
            engine.Advance();
        }
        engine.DisconnectPlayer(0, 1);
        Check.Equal(2, engine.Repair(), "Disconnect repairs status even when input value stays zero");
        Check.True(engine.TryGetInputs(2, out var disconnected), "Disconnected frames are complete");
        Check.Equal(InputStatus.Disconnected, disconnected[0].Status, "Disconnect status retained in confirmed stream");
        Check.True(!engine.TryGetInput(0, 2, out _), "Raw input after disconnect is hidden");
        engine.DisconnectPlayer(0, 3);
        Check.Equal(0, engine.Repair(), "A later disconnect report cannot move cutoff forward");
        engine.DisconnectPlayer(0, 0);
        Check.Equal(3, engine.Repair(), "An earlier agreed cutoff repairs again");
        Check.Throws<InvalidOperationException>(
            () => engine.AddInput(0, 4, 1),
            "Disconnected input cannot be extended"
        );
        engine.SetConfirmedFrame(3);
        Check.Throws<InvalidOperationException>(
            () => engine.DisconnectPlayer(0, -1),
            "Finalized disconnect cutoff cannot be changed"
        );

        var absent = new RollbackEngine<int, TestState>(1, Options(prediction: 0), new TestGame());
        absent.DisconnectPlayer(0, -1);
        Check.True(absent.Advance(), "A disconnected player with no initial input never blocks lockstep");
    }

    private static void FinalizationProtectsConsumedHistory()
    {
        var engine = new RollbackEngine<int, TestState>(1, Options(), new TestGame());
        Check.Throws<ArgumentOutOfRangeException>(
            () => engine.SetConfirmedFrame(0),
            "Cannot finalize an unconsumed frame"
        );
        engine.Advance();
        Check.Throws<InvalidOperationException>(() => engine.SetConfirmedFrame(0), "Cannot finalize predicted input");
        engine.AddInput(0, 0, 1);
        Check.Throws<InvalidOperationException>(
            () => engine.SetConfirmedFrame(0),
            "Cannot finalize before correcting a prediction"
        );
        engine.Repair();
        engine.SetConfirmedFrame(0);
        engine.SetConfirmedFrame(0);
        Check.Equal(0, engine.ConfirmedFrame, "Repeated finalization is harmless");
        Check.Throws<ArgumentOutOfRangeException>(() => engine.SetConfirmedFrame(-1), "Finalization is monotonic");
        Check.Throws<ArgumentOutOfRangeException>(
            () => engine.SetConfirmedFrame(1),
            "Finalization stops before current input frame"
        );
    }

    private static void PlayerQueuesRejectMalformedInput()
    {
        var options = Options();
        var engine = new RollbackEngine<int, TestState>(1, options, new TestGame());
        Check.Throws<ArgumentOutOfRangeException>(() => engine.LastInputFrame(-1), "Negative player handle rejected");
        Check.Throws<ArgumentOutOfRangeException>(
            () => engine.AddInput(1, 0, 1),
            "Out-of-range player handle rejected"
        );
        Check.Throws<ArgumentException>(() => engine.AddInput(0, -1, 1), "Negative input frame rejected");
        Check.Throws<ArgumentException>(() => engine.AddInput(0, 1, 1), "Skipped initial input rejected");
        engine.AddInput(0, 0, 1);
        Check.Throws<ArgumentException>(() => engine.AddInput(0, 0, 1), "Duplicate input rejected");
        Check.Throws<ArgumentException>(() => engine.AddInput(0, 2, 1), "Skipped input rejected");
        for (int frame = 1; frame < options.InputCapacity; frame++)
            engine.AddInput(0, frame, frame);
        Check.Throws<InvalidOperationException>(
            () => engine.AddInput(0, options.InputCapacity, 1),
            "Future inputs cannot overwrite retained data"
        );
        Check.True(
            engine.TryGetInput(0, options.InputCapacity - 1, out int last) && last == options.InputCapacity - 1,
            "Local future input can be retrieved for network batching"
        );
        Check.True(!engine.TryGetInputs(-1, out _), "Negative aggregate query is safe");
        Check.True(!engine.TryGetChecksum(-1, out _), "Negative checksum query is safe");
    }

    private static void SnapshotsUseFrameBoundaries()
    {
        var game = new TestGame();
        var engine = new RollbackEngine<int, TestState>(1, Options(), game);
        Check.True(!engine.TryGetChecksum(0, out _), "Initial state is captured when simulation first advances");
        engine.AddInput(0, 0, 4);
        engine.Advance();
        Check.True(
            engine.TryGetState(0, out var before) && before.State == new TestState(0, 0),
            "State frame zero precedes input zero"
        );
        Check.True(
            engine.TryGetState(1, out var after) && after.State == new TestState(game.Value, 1),
            "State frame one follows input zero"
        );
        Check.True(
            engine.TryGetChecksum(1, out ulong checksum) && checksum == unchecked((ulong)game.Value),
            "Checksum uses matching state boundary"
        );
        Check.True(!engine.TryGetState(2, out _), "Future state is unavailable");
        Check.Throws<ArgumentOutOfRangeException>(
            () => new RollbackEngine<int, TestState>(0, Options(), game),
            "At least one player required"
        );
    }

    private static void MutableSnapshotsRemainIsolated()
    {
        var game = new ArrayGame();
        var engine = new RollbackEngine<int, int[]>(1, Options(), game);
        engine.Advance();
        engine.Advance();
        Check.True(engine.TryGetState(0, out var initial), "Mutable snapshot retained");
        engine.AddInput(0, 0, 5);
        engine.Repair();
        Check.Equal(10, game.Value, "Mutable game restored and resimulated");
        Check.Equal(0, initial.State[0], "LoadState copies mutable snapshot storage before modifying it");
        engine.AddInput(0, 1, 6);
        engine.Repair();
        Check.Equal(11, game.Value, "Repeated repair can load a previously saved snapshot safely");
        Check.True(
            engine.TryGetState(1, out var first) && first.State[0] == 5,
            "Later gameplay cannot mutate prior snapshots"
        );
    }

    private static void TimeSyncMatchesReference()
    {
        foreach (
            var (local, remote, expected) in new[]
            {
                (0, 0, 0),
                (5, -5, -5),
                (-1, 1, 1),
                (-4, 4, 4),
                (-40, 40, 40),
                (2, 6, 2),
            }
        )
        {
            var sync = new TimeSync();
            for (int frame = 0; frame < 60; frame++)
                sync.Record(frame, local, remote);
            Check.Equal(expected, sync.AverageFrameAdvantage, "Thirty-frame time synchronization matches upstream");
        }
        var partial = new TimeSync();
        for (int frame = 0; frame < 10; frame++)
            partial.Record(frame, -30, 30);
        Check.Equal(10, partial.AverageFrameAdvantage, "Unfilled time sync slots contain zero");
        for (int frame = 10; frame < 40; frame++)
            partial.Record(frame, 0, 0);
        Check.Equal(0, partial.AverageFrameAdvantage, "Time sync discards overwritten samples");
        Check.Throws<ArgumentOutOfRangeException>(() => partial.Record(-1, 0, 0), "Time sync rejects negative frame");
    }

    private static void LongRunningHistoryWrapsSafely()
    {
        var game = new TestGame();
        var engine = new RollbackEngine<int, TestState>(2, Options(prediction: 2, history: 8), game);
        for (int frame = 0; frame < 1000; frame++)
        {
            engine.AddInput(0, frame, frame % 3);
            Check.True(engine.Advance(), "One-frame remote delay advances throughout ring wraparound");
            engine.AddInput(1, frame, frame % 5);
            engine.Repair();
            engine.SetConfirmedFrame(frame);
            Check.True(
                engine.TryGetInputs(frame, out var inputs) && inputs[1].Input == frame % 5,
                "Most recent input remains available"
            );
            Check.True(
                engine.TryGetState(frame + 1, out var state) && state.State.Value == game.Value,
                "Most recent state remains available"
            );
        }
        Check.Equal(1000, engine.CurrentFrame, "Long-running simulation keeps monotonic frame numbers");
        Check.True(!engine.TryGetInputs(991, out _), "History bounds checked beyond many ring wraps");
        Check.True(engine.TryGetInputs(992, out _), "Exactly HistoryFrames of consumed input remain");
    }

    private static void RandomizedDelayChangesMatchAnUnboundedModel()
    {
        var random = new Random(820173);
        for (int trial = 0; trial < 24; trial++)
        {
            var queue = new DelayedInputQueue<int>(0, 30);
            int nextExpectedFrame = 0;
            int previousOutput = 0;
            for (int frame = 0; frame < 1200; frame++)
            {
                int delay = random.Next(31);
                int input = random.Next();
                queue.SetDelay(delay);
                var expected = new List<NumberedInput<int>>();
                int destination = frame + delay;
                while (nextExpectedFrame < destination)
                    expected.Add(new NumberedInput<int>(nextExpectedFrame++, previousOutput));
                if (nextExpectedFrame == destination)
                {
                    expected.Add(new NumberedInput<int>(nextExpectedFrame++, input));
                    previousOutput = input;
                }
                var actual = queue.Submit(frame, input);
                Check.Equal(expected.Count, actual.Count, "Random delay changes preserve emitted input count");
                for (int i = 0; i < expected.Count; i++)
                    Check.Equal(
                        expected[i],
                        actual[i],
                        "Random delay changes preserve exact gap filler and dropped inputs"
                    );
            }
        }
    }

    private static void RandomizedArrivalAndPredictionMatchTheConfirmedTimeline()
    {
        const int target = 350;
        var random = new Random(938206);
        for (int trial = 0; trial < 32; trial++)
        {
            int players = 1 + trial % 8;
            int prediction = trial % 9;
            int history = prediction + 5 + trial % 5;
            var game = new TestGame();
            var engine = new RollbackEngine<int, TestState>(
                players,
                Options(prediction, history),
                game,
                trial % 2 == 0 ? null : (input, age) => age == 1 ? input : 0
            );
            var inputs = new int[players, target];
            var arrival = new int[players, target];
            var expected = new long[target];
            long value = 0;
            for (int frame = 0; frame < target; frame++)
            {
                long inputValue = 0;
                for (int player = 0; player < players; player++)
                {
                    inputs[player, frame] = random.Next(4);
                    arrival[player, frame] = frame + random.Next(1 + prediction * 2);
                    inputValue += (player + 1) * inputs[player, frame];
                }
                value = unchecked(value * 31 + frame + inputValue);
                expected[frame] = value;
            }

            var nextInput = new int[players];
            int confirmed = -1;
            for (int wall = 0; wall < target + 300 && confirmed < target - 1; wall++)
            {
                for (int player = 0; player < players; player++)
                {
                    if (wall % 79 is >= 40 and <= 48 && player % 2 == 0)
                        continue;
                    int horizon = Math.Min(target - 1, engine.CurrentFrame + random.Next(4));
                    while (nextInput[player] <= horizon && arrival[player, nextInput[player]] <= wall)
                    {
                        int frame = nextInput[player]++;
                        engine.AddInput(player, frame, inputs[player, frame]);
                    }
                }

                engine.Repair();
                if (engine.CurrentFrame < target)
                    engine.Advance();
                int available = Math.Min(engine.CurrentFrame - 1, nextInput.Min() - 1);
                int finalizationDelay = wall > target + 100 ? 0 : random.Next(4);
                int nextConfirmed = Math.Max(confirmed, available - finalizationDelay);
                engine.SetConfirmedFrame(nextConfirmed);
                for (int frame = confirmed + 1; frame <= nextConfirmed; frame++)
                {
                    Check.True(
                        engine.TryGetChecksum(frame + 1, out ulong hash),
                        "Randomized correction retains each finalized checksum"
                    );
                    Check.Equal(
                        unchecked((ulong)expected[frame]),
                        hash,
                        "Randomized arrival/order/delay matches unbounded reference"
                    );
                }
                confirmed = nextConfirmed;
            }
            Check.Equal(target, engine.CurrentFrame, "Randomized run exits every transient stall");
            Check.Equal(target - 1, confirmed, "Randomized run finalizes every frame");
            Check.Equal(expected[^1], game.Value, "Randomized final game state matches reference");
        }
    }

    private static void RandomizedDisconnectCorrectionsMatchTheFinalCutoff()
    {
        var random = new Random(243057);
        for (int trial = 0; trial < 80; trial++)
        {
            int players = 2 + trial % 7;
            int target = random.Next(12, 40);
            int firstCutoff = random.Next(4, target - 1);
            int finalCutoff = random.Next(firstCutoff);
            int leavingPlayer = random.Next(players);
            var game = new TestGame();
            var engine = new RollbackEngine<int, TestState>(players, Options(2, target + 1), game);
            var actual = new int[players, target];
            for (int frame = 0; frame < target; frame++)
            {
                for (int player = 0; player < players; player++)
                {
                    actual[player, frame] = random.Next(3);
                    engine.AddInput(player, frame, actual[player, frame]);
                }
                Check.True(
                    engine.Advance(),
                    "Disconnect model can consume fully known inputs beyond prediction budget"
                );
            }
            engine.SetConfirmedFrame(finalCutoff);
            engine.DisconnectPlayer(leavingPlayer, firstCutoff);
            Check.Equal(target - firstCutoff - 1, engine.Repair(), "First disconnect correction repairs exact suffix");
            engine.DisconnectPlayer(leavingPlayer, finalCutoff);
            Check.Equal(target - finalCutoff - 1, engine.Repair(), "Earlier agreed cutoff uses independent history");
            engine.SetConfirmedFrame(target - 1);

            long expected = 0;
            for (int frame = 0; frame < target; frame++)
            {
                long inputValue = 0;
                for (int player = 0; player < players; player++)
                {
                    bool disconnected = player == leavingPlayer && frame > finalCutoff;
                    inputValue += (player + 1) * (disconnected ? 100 : actual[player, frame]);
                }
                expected = unchecked(expected * 31 + frame + inputValue);
                Check.True(
                    engine.TryGetInputs(frame, out var retained),
                    "Disconnected timeline inputs remain available"
                );
                Check.Equal(
                    frame > finalCutoff ? InputStatus.Disconnected : InputStatus.Confirmed,
                    retained[leavingPlayer].Status,
                    "Final disconnect cutoff determines retained status"
                );
                Check.True(
                    engine.TryGetChecksum(frame + 1, out ulong hash),
                    "Disconnect repair overwrites each affected snapshot"
                );
                Check.Equal(
                    unchecked((ulong)expected),
                    hash,
                    "Final disconnect timeline matches direct reference at every boundary"
                );
            }
            Check.Equal(expected, game.Value, "Randomized disconnect final state converges");
        }
    }

    private static void AssertInputs(IReadOnlyList<NumberedInput<int>> actual, params (int Frame, int Input)[] expected)
    {
        Check.Equal(expected.Length, actual.Count, "Delayed input count");
        for (int i = 0; i < expected.Length; i++)
            Check.Equal(
                new NumberedInput<int>(expected[i].Frame, expected[i].Input),
                actual[i],
                "Delayed input value and frame"
            );
    }

    private static long Expected(params (int Input, bool Disconnected)[] inputs)
    {
        long value = 0;
        for (int frame = 0; frame < inputs.Length; frame++)
            value = unchecked(value * 31 + frame + inputs[frame].Input + (inputs[frame].Disconnected ? 100 : 0));
        return value;
    }

    private readonly record struct TestState(long Value, int NextFrame);

    private sealed class TestGame : IRollbackGame<int, TestState>
    {
        private int nextFrame;
        public long Value { get; private set; }
        public int Resimulations { get; private set; }

        public SavedState<TestState> SaveState() => new(new TestState(Value, nextFrame), unchecked((ulong)Value));

        public void LoadState(TestState state)
        {
            Value = state.Value;
            nextFrame = state.NextFrame;
        }

        public void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<int>> inputs, bool isResimulation)
        {
            Check.Equal(nextFrame, frame, "Save/load frame boundary matches game state");
            long inputValue = 0;
            for (int player = 0; player < inputs.Length; player++)
                inputValue +=
                    (player + 1)
                    * (inputs[player].Input + (inputs[player].Status == InputStatus.Disconnected ? 100 : 0));
            Value = unchecked(Value * 31 + frame + inputValue);
            nextFrame++;
            if (isResimulation)
                Resimulations++;
        }
    }

    private sealed class ArrayGame : IRollbackGame<int, int[]>
    {
        private int[] state = [0];
        public int Value => state[0];

        public SavedState<int[]> SaveState() => new((int[])state.Clone());

        public void LoadState(int[] snapshot) => state = (int[])snapshot.Clone();

        public void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<int>> inputs, bool isResimulation) =>
            state[0] += inputs[0].Input;
    }
}
