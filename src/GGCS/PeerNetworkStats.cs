namespace GGCS;

public readonly record struct PeerNetworkStats(
    int PeerId,
    SessionState State,
    double RoundTripMilliseconds,
    double FramesAhead,
    int PendingInputFrames,
    int LastReceivedFrame,
    int LastAcknowledgedFrame,
    long PacketsSent,
    long PacketsReceived,
    long BytesSent,
    long BytesReceived,
    long InvalidPackets,
    long StalePackets
);
