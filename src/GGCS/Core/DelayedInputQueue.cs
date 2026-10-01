namespace GGCS.Core;

internal sealed class DelayedInputQueue<TInput>
    where TInput : unmanaged
{
    private readonly int maximumDelay;
    private int delay;
    private int lastUserFrame = -1;
    private int lastOutputFrame = -1;
    private TInput lastOutput;

    public DelayedInputQueue(int delay, int maxDelay)
    {
        if (maxDelay < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDelay));
        maximumDelay = maxDelay;
        SetDelay(delay);
    }

    public void SetDelay(int delay)
    {
        if (delay < 0 || delay > maximumDelay)
            throw new ArgumentOutOfRangeException(nameof(delay));
        this.delay = delay;
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
            output[i] = new NumberedInput<TInput>(lastOutputFrame + i + 1, lastOutput);

        output[^1] = new NumberedInput<TInput>(outputFrame, input);
        lastOutputFrame = outputFrame;
        lastOutput = input;
        return output;
    }
}
