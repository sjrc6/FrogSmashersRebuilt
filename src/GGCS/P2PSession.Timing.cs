namespace GGCS;

public sealed partial class P2PSession<TInput, TState>
    where TInput : unmanaged
{
    private long lastDelayChange;
    private long lastPredictionPressure;
    private long nextPressureIncrease;
    private int lastDelayChangeFrame = -1;
    private bool timingFrozen;
    private RollbackTiming? pendingTiming;
    public RollbackTiming Timing { get; private set; }
    public int ExtraDelayFrames { get; private set; }
    public int EffectiveDelayFrames => Timing.DelayFrames + ExtraDelayFrames;

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
        ExtraDelayFrames = Math.Min(ExtraDelayFrames, timing.MaxExtraDelayFrames);
        ApplyTimingDelay();
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

    public void RestoreExtraDelay(int frames)
    {
        if (CurrentFrame != 0 || submittedFrame >= 0 || frames < 0 || frames > Timing.MaxExtraDelayFrames)
            throw new InvalidOperationException(
                "Restore temporary delay within its limit before advancing the session."
            );
        ExtraDelayFrames = frames;
        ApplyTimingDelay();
    }

    public int PendingInputCount(int handle, int boundary) =>
        delays.TryGetValue(handle, out var queue) ? Math.Max(0, queue.LastOutputFrame - boundary + 1) : 0;

    private void ApplyTimingDelay()
    {
        if (timingFrozen)
            return;
        foreach (var queue in delays.Values)
            queue.SetDelay(EffectiveDelayFrames);
        lastDelayChange = clock.NowMilliseconds;
        lastDelayChangeFrame = CurrentFrame;
    }

    private void UpdateTiming()
    {
        if (
            timingFrozen
            || IsInputObserver
            || !hasSynchronized
            || HasPendingDisconnect
            || Timing.MaxExtraDelayFrames == 0
        )
            return;
        long now = clock.NowMilliseconds;
        int reserve = Math.Min(4, options.MaxPredictionFrames);
        int estimatedDelay = 0;
        double roundTrip = 0;
        foreach (var peer in RequiredPeerLinks().Where(peer => peer.State == SessionState.Running))
        {
            var stats = peer.Stats;
            roundTrip = Math.Max(roundTrip, stats.RoundTripMilliseconds);
            int required =
                (int)Math.Ceiling(stats.RoundTripMilliseconds * options.FramesPerSecond / 2000)
                + Timing.DonationFrames
                - stats.DonationFrames
                - Timing.DelayFrames
                - options.MaxPredictionFrames
                + reserve;
            estimatedDelay = Math.Max(estimatedDelay, required);
        }
        estimatedDelay = Math.Clamp(estimatedDelay, 0, Timing.MaxExtraDelayFrames);
        int prediction = PredictionDepth;
        int pressureThreshold = options.MaxPredictionFrames - reserve;
        bool acceptedFreshInput = delays.Values.All(queue => queue.LastAcceptedUserFrame > lastDelayChangeFrame);
        long feedbackMilliseconds = (long)
            Math.Ceiling(roundTrip / 2 + 1000d * Core.TimeSync.WindowSize / options.FramesPerSecond);
        if (peers.Count > 0 && prediction > 0 && prediction >= pressureThreshold)
        {
            lastPredictionPressure = now;
            if (
                !acceptedFreshInput
                || ExtraDelayFrames > 0 && now < nextPressureIncrease && estimatedDelay <= ExtraDelayFrames
            )
                return;
            int desired = Math.Max(estimatedDelay, ExtraDelayFrames + Math.Max(2, prediction - pressureThreshold));
            desired = Math.Min(desired, Timing.MaxExtraDelayFrames);
            if (desired == ExtraDelayFrames)
                return;
            ExtraDelayFrames = desired;
            nextPressureIncrease = now + Math.Max(100, feedbackMilliseconds);
            ApplyTimingDelay();
        }
        else if (
            estimatedDelay < ExtraDelayFrames
            && prediction + 2 < pressureThreshold
            && now - lastPredictionPressure >= Math.Max(100, (long)Math.Ceiling(roundTrip / 2))
            && now - lastDelayChange >= 50
            && acceptedFreshInput
        )
        {
            ExtraDelayFrames--;
            ApplyTimingDelay();
        }
    }
}
