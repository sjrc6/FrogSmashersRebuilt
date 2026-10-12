namespace GGCS;

public sealed record RollbackTiming
{
    public int DelayFrames { get; init; } = 2;
    public int DonationFrames { get; init; }

    internal void Validate(SessionOptions options)
    {
        if (
            DelayFrames < 0
            || DelayFrames > options.MaxInputDelay
            || DonationFrames < 0
            || DonationFrames > options.MaxPredictionFrames
        )
            throw new ArgumentOutOfRangeException(nameof(RollbackTiming), "Invalid rollback timing limits.");
    }
}
