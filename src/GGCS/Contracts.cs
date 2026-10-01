using System.Diagnostics;

namespace GGCS;

public enum InputStatus
{
    Confirmed,
    Predicted,
    Disconnected,
}

public readonly record struct PlayerInput<TInput>(TInput Input, InputStatus Status)
    where TInput : unmanaged;

public readonly record struct NumberedInput<TInput>(int Frame, TInput Input)
    where TInput : unmanaged;

public readonly record struct SavedState<TState>(TState State, ulong? Checksum = null);

public readonly record struct Player(int Handle, int PeerId);

public readonly record struct Datagram(int PeerId, ReadOnlyMemory<byte> Data);

public interface IRollbackGame<TInput, TState>
    where TInput : unmanaged
{
    SavedState<TState> SaveState();
    void LoadState(TState state);
    void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<TInput>> inputs, bool isResimulation);
}

public interface IInputCodec<TInput>
    where TInput : unmanaged
{
    int Size { get; }
    void Encode(TInput input, Span<byte> destination);
    TInput Decode(ReadOnlySpan<byte> source);
}

public interface ITransport
{
    void Send(int peerId, ReadOnlySpan<byte> packet);
    bool TryReceive(out Datagram datagram);
}

public interface IClock
{
    long NowMilliseconds { get; }
}

public sealed class MonotonicClock : IClock
{
    private readonly long started = Stopwatch.GetTimestamp();
    public long NowMilliseconds => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}

public enum SessionState
{
    Synchronizing,
    Running,
    Disconnected,
}

public enum AdvanceStatus
{
    Advanced,
    Synchronizing,
    PredictionLimit,
    Disconnected,
    WaitingForInput,
    InputBufferFull,
    DisconnectAgreement,
}

public enum SessionEventKind
{
    Synchronizing,
    Synchronized,
    NetworkInterrupted,
    NetworkResumed,
    Disconnected,
    DesyncDetected,
    ProtocolError,
    SpectatorTooFarBehind,
    Excluded,
}

public readonly record struct SessionEvent(SessionEventKind Kind, int PeerId, int Frame = -1, string? Detail = null);

public sealed record SessionOptions
{
    public int FramesPerSecond { get; init; } = 60;
    public int MaxPredictionFrames { get; init; } = 8;
    public int HistoryFrames { get; init; } = 240;
    public int InputDelay { get; init; } = 0;
    public int MaxInputDelay { get; init; } = 30;
    public int DisconnectNotifyMilliseconds { get; init; } = 1000;
    public int DisconnectTimeoutMilliseconds { get; init; } = 5000;
    public int RetryMilliseconds { get; init; } = 100;
    public int KeepAliveMilliseconds { get; init; } = 200;
    public int QualityReportMilliseconds { get; init; } = 200;
    public int SynchronizationRoundTrips { get; init; } = 5;
    public int ChecksumInterval { get; init; } = 60;
    public int MaxPacketBytes { get; init; } = 1200;
    public int MaxPacketsPerPoll { get; init; } = 4096;
    public int SpectatorCatchUpFrames { get; init; } = 8;
    public int SpectatorBufferFrames { get; init; } = 0;
    public int InputCapacity => checked(HistoryFrames + MaxPredictionFrames + MaxInputDelay + 32);

    internal void Validate()
    {
        if (
            FramesPerSecond is < 1 or > 1000
            || MaxPredictionFrames is < 0 or > 10000
            || HistoryFrames < Math.Max(2, MaxPredictionFrames + 1)
            || HistoryFrames > 100000
            || MaxInputDelay is < 0 or > 10000
            || InputDelay < 0
            || InputDelay > MaxInputDelay
            || DisconnectNotifyMilliseconds < 1
            || DisconnectTimeoutMilliseconds <= DisconnectNotifyMilliseconds
            || RetryMilliseconds < 1
            || KeepAliveMilliseconds < 1
            || QualityReportMilliseconds < 1
            || SynchronizationRoundTrips is < 1 or > 32
            || ChecksumInterval < 0
            || MaxPacketBytes is < 256 or > 65507
            || MaxPacketsPerPoll is < 1 or > 100000
            || SpectatorCatchUpFrames is < 1 or > 1000
            || SpectatorBufferFrames < 0
            || SpectatorBufferFrames >= HistoryFrames
        )
            throw new ArgumentOutOfRangeException(nameof(SessionOptions), "Invalid rollback session limits.");
    }
}
