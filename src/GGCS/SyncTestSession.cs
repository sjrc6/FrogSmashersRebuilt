namespace GGCS;

public sealed class DeterminismException(int frame, ulong expected, ulong actual)
    : Exception($"State differs after replaying frame {frame}: expected {expected:x16}, got {actual:x16}.")
{
    public int Frame { get; } = frame;
    public ulong Expected { get; } = expected;
    public ulong Actual { get; } = actual;
}

public sealed class SyncTestSession<TInput, TState>
    where TInput : unmanaged
{
    private readonly int playerCount;
    private readonly int checkDistance;
    private readonly IRollbackGame<TInput, TState> game;
    private readonly SavedState<TState>[] states;
    private readonly PlayerInput<TInput>[][] inputs;

    public SyncTestSession(int playerCount, IRollbackGame<TInput, TState> game, int checkDistance = 8)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (playerCount is < 1 or > 32 || checkDistance is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        this.playerCount = playerCount;
        this.game = game;
        this.checkDistance = checkDistance;
        states = new SavedState<TState>[checkDistance + 1];
        inputs = new PlayerInput<TInput>[checkDistance + 1][];
        states[0] = Capture();
    }

    public int CurrentFrame { get; private set; }
    public long VerifiedFrames { get; private set; }

    public void AdvanceFrame(ReadOnlySpan<TInput> localInputs)
    {
        if (localInputs.Length != playerCount)
            throw new ArgumentException("Supply one input for each player.", nameof(localInputs));
        var frameInputs = new PlayerInput<TInput>[playerCount];
        for (int i = 0; i < frameInputs.Length; i++)
            frameInputs[i] = new(localInputs[i], InputStatus.Confirmed);
        inputs[CurrentFrame % inputs.Length] = frameInputs;
        game.AdvanceFrame(CurrentFrame, frameInputs, false);
        CurrentFrame++;
        states[CurrentFrame % states.Length] = Capture();
        int start = Math.Max(0, CurrentFrame - checkDistance);
        game.LoadState(states[start % states.Length].State);
        for (int frame = start; frame < CurrentFrame; frame++)
        {
            game.AdvanceFrame(frame, inputs[frame % inputs.Length], true);
            ulong expected = states[(frame + 1) % states.Length].Checksum!.Value;
            ulong actual = Capture().Checksum!.Value;
            if (expected != actual)
                throw new DeterminismException(frame + 1, expected, actual);
            VerifiedFrames++;
        }
    }

    private SavedState<TState> Capture()
    {
        var state = game.SaveState();
        if (state.Checksum is null)
            throw new InvalidOperationException("Determinism testing requires a checksum on every saved state.");
        return state;
    }
}
