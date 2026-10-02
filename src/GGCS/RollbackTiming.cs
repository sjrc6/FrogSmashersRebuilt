namespace GGCS;

public sealed record RollbackTiming
{
    public int DelayFrames { get; init; } = 2;
    public int DonationFrames { get; init; }
    public int MaxExtraDelayFrames { get; init; }

    internal void Validate(SessionOptions options)
    {
        if (
            DelayFrames < 0
            || DonationFrames < 0
            || DonationFrames > options.MaxPredictionFrames
            || MaxExtraDelayFrames < 0
            || DelayFrames + (long)MaxExtraDelayFrames > options.MaxInputDelay
        )
            throw new ArgumentOutOfRangeException(nameof(RollbackTiming), "Invalid rollback timing limits.");
    }
}
