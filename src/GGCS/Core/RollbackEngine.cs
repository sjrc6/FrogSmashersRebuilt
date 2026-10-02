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
        public readonly List<(int First, int Last)> PreviousDisconnects = new();

        public bool DisconnectedAt(int frame) =>
            frame > DisconnectFrame || PreviousDisconnects.Any(range => frame >= range.First && frame <= range.Last);

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
    private readonly SessionParticipation<TInput>? participation;
    private readonly int initialFrame;
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
        Func<TInput, int, TInput>? predictor = null,
        SessionParticipation<TInput>? participation = null,
        int initialFrame = 0
    )
    {
        if (playerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(game);
        if (
            initialFrame < 0
            || initialFrame == int.MaxValue
            || participation != null
                && (
                    participation.AuthorityHandle < 0
                    || participation.AuthorityHandle >= playerCount
                    || participation.Players == null
                )
        )
            throw new ArgumentException("Invalid initial frame or participation authority.");
        options.Validate();
        this.options = options;
        this.game = game;
        this.predictor = predictor ?? ((input, _) => input);
        this.participation = participation;
        this.initialFrame = initialFrame;
        CurrentFrame = initialFrame;
        ConfirmedFrame = initialFrame - 1;
        histories = Enumerable.Range(0, playerCount).Select(_ => new InputHistory(options.InputCapacity)).ToArray();
        foreach (var history in histories)
            history.LastFrame = initialFrame - 1;
        stateFrames = Enumerable.Repeat(-1, options.HistoryFrames + 1).ToArray();
        states = new SavedState<TState>[stateFrames.Length];
        usedFrames = Enumerable.Repeat(-1, options.HistoryFrames).ToArray();
        usedInputs = Enumerable
            .Range(0, options.HistoryFrames)
            .Select(_ => new PlayerInput<TInput>[playerCount])
            .ToArray();
        if (participation != null)
            _ = Participates(participation.AuthorityHandle, initialFrame);
    }

    public int LastInputFrame(int player) => History(player).LastFrame;

    public bool Participates(int player, int frame)
    {
        if (participation == null)
            return true;
        var authority = History(participation.AuthorityHandle);
        TInput input =
            authority.TryGet(frame, out var actual) ? actual
            : frame > authority.LastFrame && authority.LastFrame >= initialFrame ? authority.LastInput
            : participation.InitialInput;
        uint mask = participation.Players(input);
        if (
            (mask & (1u << participation.AuthorityHandle)) == 0
            || (histories.Length < 32 && mask >> histories.Length != 0)
        )
            throw new InvalidDataException(
                "Participation must include its authority and contain only registered handles."
            );
        return (mask & (1u << player)) != 0;
    }

    public int PredictionForPlayer(int player)
    {
        var history = History(player);
        if (participation == null)
            return CurrentFrame > history.DisconnectFrame ? 0 : Math.Max(0, CurrentFrame - history.LastFrame - 1);
        if (!Participates(player, CurrentFrame) || history.DisconnectedAt(CurrentFrame))
            return 0;
        int first = CurrentFrame;
        while (first > Math.Max(initialFrame, history.LastFrame + 1) && Participates(player, first - 1))
            first--;
        return CurrentFrame - first;
    }

    public void StartInput(int player, int frame)
    {
        var history = History(player);
        if (frame < Math.Max(initialFrame, OldestRetainedFrame))
            throw new ArgumentOutOfRangeException(nameof(frame));
        for (
            int missing = Math.Min(frame, Math.Max(initialFrame, history.LastFrame + 1));
            missing < CurrentFrame;
            missing++
        )
            if (!history.DisconnectedAt(missing) && Participates(player, missing))
                throw new InvalidOperationException("Cannot skip input from an active participant.");
        if (history.DisconnectFrame != int.MaxValue)
            history.PreviousDisconnects.Add((history.DisconnectFrame + 1, frame - 1));
        history.PreviousDisconnects.RemoveAll(range => range.Last < OldestRetainedFrame);
        for (int index = 0; index < history.Frames.Length; index++)
            if (history.Frames[index] >= frame)
                history.Frames[index] = -1;
        history.LastFrame = frame - 1;
        history.LastInput = default;
        history.DisconnectFrame = int.MaxValue;
    }

    public bool TryGetInput(int player, int frame, out TInput input)
    {
        InputHistory history = History(player);
        input = default;
        return frame >= OldestRetainedFrame && !history.DisconnectedAt(frame) && history.TryGet(frame, out input);
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

        if (frame < CurrentFrame && Participates(player, frame))
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

    public void SeedDisconnect(int player, int cutoff)
    {
        if (CurrentFrame != initialFrame || cutoff < -1 || cutoff >= initialFrame)
            throw new ArgumentOutOfRangeException(nameof(cutoff));
        History(player).DisconnectFrame = cutoff;
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
            for (int player = 0; player < histories.Length; player++)
            {
                var history = histories[player];
                if (Participates(player, next) && !history.DisconnectedAt(next) && !history.TryGet(next, out _))
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

        for (int player = 0; player < histories.Length; player++)
        {
            var history = histories[player];
            if (
                history.LastFrame < history.DisconnectFrame
                && history.LastFrame < CurrentFrame
                && Participates(player, CurrentFrame)
                && PredictionForPlayer(player) >= options.MaxPredictionFrames
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
            if (!Participates(player, frame) || history.DisconnectedAt(frame))
                result[player] = new PlayerInput<TInput>(default, InputStatus.Disconnected);
            else if (history.TryGet(frame, out TInput input))
                result[player] = new PlayerInput<TInput>(input, InputStatus.Confirmed);
            else
                return false;
        }
        inputs = result;
        return true;
    }

    public bool HasConfirmedInputs(int frame)
    {
        for (int player = 0; player < histories.Length; player++)
            if (
                Participates(player, frame)
                && !histories[player].DisconnectedAt(frame)
                && !histories[player].TryGet(frame, out _)
            )
                return false;
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
            if (!Participates(player, CurrentFrame) || history.DisconnectedAt(CurrentFrame))
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
