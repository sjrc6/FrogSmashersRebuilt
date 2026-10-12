namespace GGCS;

public sealed partial class P2PSession<TInput, TState>
    where TInput : unmanaged
{
    private bool timingFrozen;
    private RollbackTiming? pendingTiming;
    public RollbackTiming Timing { get; private set; }

    public void SetTiming(RollbackTiming timing)
    {
        timing.Validate(options);
        if (timingFrozen)
        {
            pendingTiming = timing;
            return;
        }
        if (timing == Timing)
            return;
        Timing = timing;
        foreach (var queue in delays.Values)
            queue.SetDelay(timing.DelayFrames);
    }

    public void FreezeTiming(bool frozen)
    {
        timingFrozen = frozen;
        if (!frozen && pendingTiming is { } pending)
        {
            pendingTiming = null;
            SetTiming(pending);
        }
    }

    public int PendingInputCount(int handle, int boundary) =>
        delays.TryGetValue(handle, out var queue) ? Math.Max(0, queue.LastOutputFrame - boundary + 1) : 0;
}
