using FrogSmashers.Core;
using GGCS;

namespace FrogSmashers.Network;

public interface IRollbackSession : IDisposable
{
    World World { get; }
    byte[] PreviousSnapshot { get; }
    string? Error { get; }
    bool IsTransportFailure { get; }
    int LocalPeer { get; }
    int[] LocalSlots { get; }
    int PlayerSlot(int handle);
    int InputHandle(int slot);
    int HostCommandHandle { get; }
    long ConfirmedFrame { get; }
    int PredictionDepth { get; }
    int MaxPrediction { get; }
    IReadOnlyList<int> WaitingForInputPlayers { get; }
    bool WaitingForHostInputs { get; }
    int PlayerPeer(int handle);
    int PredictionForPlayer(int handle);
    long RollbackCount { get; }
    long ResimulatedTicks { get; }
    long LastRollbackFromFrame { get; }
    int FramesAheadOfPeers { get; }
    double FrameDurationMultiplier { get; }
    int BufferedFrames { get; }
    IReadOnlyList<int> AcceptedLocalSlots { get; }
    bool LocalInputSubmitted { get; }
    long LastSubmittedTick { get; }
    long StartTick { get; }
    string WaitReason { get; }
    SessionState State { get; }
    IReadOnlyList<PeerNetworkStats> PeerStats { get; }
    int RejectedPackets { get; }
    IEnumerable<SimulationEvent> EventsSince(long frame);
    RollbackPreferences RollbackSettings { get; }
    int ExtraDelayFrames { get; }
    int EffectiveDelayFrames { get; }
    void SetTiming(RollbackPreferences preferences);
    void StopAtTick(long? stateTick);
    bool TryGetConfirmedCheckpoint(long stateTick, out byte[] snapshot);
    bool AllPeersConfirmed(long inputTick);
    void DisconnectPeer(int peer);
    void Poll();
    bool TryAdvance(RollbackInput[] localInputs);
}
