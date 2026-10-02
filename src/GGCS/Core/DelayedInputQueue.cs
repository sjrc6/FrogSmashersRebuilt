namespace GGCS.Core;

internal sealed class DelayedInputQueue<TInput>
    where TInput : unmanaged
{
    private readonly int maximumDelay;
    private int delay;
    private int lastUserFrame = -1;
    private int lastOutputFrame = -1;
    private TInput lastOutput;
    private readonly Func<TInput, int, TInput> padInput;

    public DelayedInputQueue(
        int delay,
        int maxDelay,
        Func<TInput, int, TInput>? padInput = null,
        int initialFrame = 0,
        TInput initialInput = default
    )
    {
        if (maxDelay < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDelay));
        maximumDelay = maxDelay;
        this.padInput = padInput ?? ((input, _) => input);
        lastUserFrame = lastOutputFrame = initialFrame - 1;
        lastOutput = initialInput;
        SetDelay(delay);
    }

    public int Delay => delay;
    public int LastOutputFrame => lastOutputFrame;
    public int LastAcceptedUserFrame { get; private set; } = -1;

    public void SetDelay(int delay)
    {
        if (delay < 0 || delay > maximumDelay)
            throw new ArgumentOutOfRangeException(nameof(delay));
        this.delay = delay;
    }

    public void Seed(ReadOnlySpan<TInput> inputs)
    {
        if (lastUserFrame != -1 || lastOutputFrame != -1 || inputs.Length > maximumDelay)
            throw new InvalidOperationException(
                "Seed pending inputs within the delay capacity before submitting any frame."
            );
        lastOutputFrame = inputs.Length - 1;
        lastOutput = inputs.IsEmpty ? default : inputs[^1];
    }

    public IReadOnlyList<NumberedInput<TInput>> Submit(int userFrame, TInput input)
    {
        if (userFrame != checked(lastUserFrame + 1))
            throw new ArgumentException("Input must be submitted once per consecutive game frame.", nameof(userFrame));

        int outputFrame = checked(userFrame + delay);
        lastUserFrame = userFrame;
        if (outputFrame <= lastOutputFrame)
            return Array.Empty<NumberedInput<TInput>>();

        var output = new NumberedInput<TInput>[outputFrame - lastOutputFrame];
        for (int i = 0; i < output.Length - 1; i++)
            output[i] = new NumberedInput<TInput>(lastOutputFrame + i + 1, padInput(lastOutput, i + 1));

        output[^1] = new NumberedInput<TInput>(outputFrame, input);
        lastOutputFrame = outputFrame;
        lastOutput = input;
        LastAcceptedUserFrame = userFrame;
        return output;
    }
}
