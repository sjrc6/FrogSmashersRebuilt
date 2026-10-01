namespace GGCS.Core;

internal sealed class RollbackEngine<TInput, TState>
    where TInput : unmanaged
{
    private sealed class InputHistory(int capacity)
    {
        public readonly int[] Frames = Enumerable.Repeat(-1, capacity).ToArray();
        public readonly TInput[] Inputs = new TInput[capacity];
        public int LastFrame = -1;
        public TInput LastInput;
        public int DisconnectFrame = int.MaxValue;

        public bool TryGet(int frame, out TInput input)
        {
            int index = frame % Frames.Length;
            input = Inputs[index];
            return Frames[index] == frame;
        }
    }

    private readonly SessionOptions options;
    private readonly IRollbackGame<TInput, TState> game;
    private readonly Func<TInput, int, TInput> predictor;
    private readonly InputHistory[] histories;
    private readonly int[] stateFrames;
    private readonly SavedState<TState>[] states;
    private readonly int[] usedFrames;
    private readonly PlayerInput<TInput>[][] usedInputs;
    private int firstIncorrectFrame = int.MaxValue;

    public int CurrentFrame { get; private set; }
    public int ConfirmedFrame { get; private set; } = -1;
    private int OldestRetainedFrame => Math.Max(0, CurrentFrame - options.HistoryFrames);

    public RollbackEngine(
        int playerCount,
        SessionOptions options,
        IRollbackGame<TInput, TState> game,
        Func<TInput, int, TInput>? predictor = null
    )
    {
        if (playerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(game);
        options.Validate();
        this.options = options;
        this.game = game;
        this.predictor = predictor ?? ((input, _) => input);
        histories = Enumerable.Range(0, playerCount).Select(_ => new InputHistory(options.InputCapacity)).ToArray();
        stateFrames = Enumerable.Repeat(-1, options.HistoryFrames + 1).ToArray();
        states = new SavedState<TState>[stateFrames.Length];
        usedFrames = Enumerable.Repeat(-1, options.HistoryFrames).ToArray();
        usedInputs = Enumerable
            .Range(0, options.HistoryFrames)
            .Select(_ => new PlayerInput<TInput>[playerCount])
            .ToArray();
    }

    public int LastInputFrame(int player) => History(player).LastFrame;

    public bool TryGetInput(int player, int frame, out TInput input)
    {
        InputHistory history = History(player);
        input = default;
        return frame >= OldestRetainedFrame && frame <= history.DisconnectFrame && history.TryGet(frame, out input);
    }

    public void AddInput(int player, int frame, TInput input)
    {
        InputHistory history = History(player);
        if (frame < 0 || frame != checked(history.LastFrame + 1))
            throw new ArgumentException(
                "Player inputs must have consecutive frame numbers starting at zero.",
                nameof(frame)
            );
        if (frame > history.DisconnectFrame)
            throw new InvalidOperationException("Cannot add input after a player's disconnect frame.");
        if (frame < OldestRetainedFrame || (long)frame - OldestRetainedFrame >= history.Frames.Length)
            throw new InvalidOperationException("Input falls outside the retained history capacity.");

        if (frame < CurrentFrame)
        {
            int usedIndex = frame % usedFrames.Length;
            if (usedFrames[usedIndex] != frame)
                throw new InvalidOperationException("The simulated input needed for comparison is no longer retained.");
            TInput previousInput = usedInputs[usedIndex][player].Input;
            if (!EqualityComparer<TInput>.Default.Equals(previousInput, input))
            {
                if (frame <= ConfirmedFrame)
                    throw new InvalidOperationException("Cannot change a finalized frame.");
                firstIncorrectFrame = Math.Min(firstIncorrectFrame, frame);
            }
        }

        int index = frame % history.Frames.Length;
        history.Frames[index] = frame;
        history.Inputs[index] = input;
        history.LastFrame = frame;
        history.LastInput = input;
    }

    public void DisconnectPlayer(int player, int lastFrame)
    {
        InputHistory history = History(player);
        if (lastFrame < -1)
            throw new ArgumentOutOfRangeException(nameof(lastFrame));
        if (lastFrame >= history.DisconnectFrame)
            return;
        if (lastFrame < ConfirmedFrame)
            throw new InvalidOperationException("A disconnect cannot change finalized inputs.");

        history.DisconnectFrame = lastFrame;
        int firstDisconnectedFrame = checked(lastFrame + 1);
        if (firstDisconnectedFrame < CurrentFrame)
            firstIncorrectFrame = Math.Min(firstIncorrectFrame, firstDisconnectedFrame);
    }

    public void SetConfirmedFrame(int frame)
    {
        if (frame < ConfirmedFrame || frame >= CurrentFrame)
            throw new ArgumentOutOfRangeException(
                nameof(frame),
                "Only simulated frames may be finalized, in increasing order."
            );
        if (firstIncorrectFrame <= frame)
            throw new InvalidOperationException("Repair incorrect predictions before finalizing frames.");
        for (int next = ConfirmedFrame + 1; next <= frame; next++)
        {
            foreach (InputHistory history in histories)
            {
                if (next <= history.DisconnectFrame && !history.TryGet(next, out _))
                    throw new InvalidOperationException("A predicted input cannot be finalized.");
            }
        }
        ConfirmedFrame = frame;
    }

    public int Repair()
    {
        if (firstIncorrectFrame == int.MaxValue)
            return 0;

        int repairFrame = firstIncorrectFrame;
        int targetFrame = CurrentFrame;
        int stateIndex = repairFrame % states.Length;
        if (stateFrames[stateIndex] != repairFrame || repairFrame < OldestRetainedFrame)
            throw new InvalidOperationException("The state needed to repair a prediction is no longer retained.");
        if (repairFrame <= ConfirmedFrame)
            throw new InvalidOperationException("Cannot repair a finalized frame.");

        game.LoadState(states[stateIndex].State);
        CurrentFrame = repairFrame;
        firstIncorrectFrame = int.MaxValue;
        while (CurrentFrame < targetFrame)
            SimulateFrame(isResimulation: true);
        return targetFrame - repairFrame;
    }

    public bool Advance()
    {
        Repair();
        if (CurrentFrame - ConfirmedFrame - 1 >= options.HistoryFrames)
            return false;

        foreach (InputHistory history in histories)
        {
            if (
                history.LastFrame < history.DisconnectFrame
                && CurrentFrame - history.LastFrame - 1 >= options.MaxPredictionFrames
            )
                return false;
        }

        SimulateFrame(isResimulation: false);
        return true;
    }

    public bool TryGetInputs(int frame, out PlayerInput<TInput>[] inputs)
    {
        inputs = Array.Empty<PlayerInput<TInput>>();
        if (frame < OldestRetainedFrame || frame > CurrentFrame)
            return false;

        var result = new PlayerInput<TInput>[histories.Length];
        for (int player = 0; player < histories.Length; player++)
        {
            InputHistory history = histories[player];
            if (frame > history.DisconnectFrame)
                result[player] = new PlayerInput<TInput>(default, InputStatus.Disconnected);
            else if (history.TryGet(frame, out TInput input))
                result[player] = new PlayerInput<TInput>(input, InputStatus.Confirmed);
            else
                return false;
        }
        inputs = result;
        return true;
    }

    public bool TryGetChecksum(int stateFrame, out ulong checksum)
    {
        checksum = 0;
        if (stateFrame < OldestRetainedFrame || stateFrame > CurrentFrame)
            return false;
        int index = stateFrame % stateFrames.Length;
        if (stateFrames[index] != stateFrame || !states[index].Checksum.HasValue)
            return false;
        checksum = states[index].Checksum!.Value;
        return true;
    }

    public bool TryGetState(int stateFrame, out SavedState<TState> state)
    {
        state = default;
        if (stateFrame < OldestRetainedFrame || stateFrame > CurrentFrame)
            return false;
        int index = stateFrame % stateFrames.Length;
        if (stateFrames[index] != stateFrame)
            return false;
        state = states[index];
        return true;
    }

    private void SimulateFrame(bool isResimulation)
    {
        int usedIndex = CurrentFrame % usedFrames.Length;
        PlayerInput<TInput>[] inputs = usedInputs[usedIndex];
        for (int player = 0; player < histories.Length; player++)
        {
            InputHistory history = histories[player];
            if (CurrentFrame > history.DisconnectFrame)
                inputs[player] = new PlayerInput<TInput>(default, InputStatus.Disconnected);
            else if (history.TryGet(CurrentFrame, out TInput input))
                inputs[player] = new PlayerInput<TInput>(input, InputStatus.Confirmed);
            else
            {
                int predictionAge = CurrentFrame - history.LastFrame;
                TInput prediction = history.LastFrame < 0 ? default : predictor(history.LastInput, predictionAge);
                inputs[player] = new PlayerInput<TInput>(prediction, InputStatus.Predicted);
            }
        }

        if (stateFrames[CurrentFrame % states.Length] != CurrentFrame)
            SaveCurrentState();
        usedFrames[usedIndex] = CurrentFrame;
        game.AdvanceFrame(CurrentFrame, inputs, isResimulation);
        CurrentFrame = checked(CurrentFrame + 1);
        SaveCurrentState();
    }

    private void SaveCurrentState()
    {
        int index = CurrentFrame % states.Length;
        SavedState<TState> snapshot = game.SaveState();
        stateFrames[index] = CurrentFrame;
        states[index] = snapshot;
    }

    private InputHistory History(int player)
    {
        if ((uint)player >= histories.Length)
            throw new ArgumentOutOfRangeException(nameof(player));
        return histories[player];
    }
}
