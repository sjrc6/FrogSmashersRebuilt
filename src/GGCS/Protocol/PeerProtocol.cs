using GGCS.Core;

namespace GGCS.Protocol;

internal readonly record struct ConnectionStatus(bool Disconnected, int LastFrame);

internal readonly record struct InputPacket(int Frame, byte[] Data);

internal readonly record struct ChecksumPacket(int Frame, ulong Checksum);

internal sealed class PeerProtocol
{
    private readonly int peerId;
    private readonly ulong sessionId;
    private readonly ulong configurationId;
    private readonly int sendInputSize;
    private readonly int receiveInputSize;
    private readonly int initialFrame;
    private readonly SessionOptions options;
    private readonly IClock clock;
    private readonly ITransport transport;
    private readonly SortedDictionary<int, byte[]> pendingInputs = new();
    private readonly SortedDictionary<int, byte[]> receivedHistory = new();
    private readonly Queue<InputPacket> receivedInputs = new();
    private readonly Queue<SessionEvent> events = new();
    private readonly SortedDictionary<int, ulong> sentChecksums = new();
    private readonly SortedDictionary<int, ulong> receivedChecksumHistory = new();
    private readonly SortedSet<long> pendingPings = new();
    private readonly Queue<ChecksumPacket> receivedChecksums = new();
    private readonly TimeSync timeSync = new();
    private readonly ConnectionStatus[] localStatuses;
    private readonly ConnectionStatus[] remoteStatuses;
    private uint challenge = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
    private int synchronizedRoundTrips;
    private int currentFrame;
    private RollbackTiming timing = new() { DelayFrames = 0, MaxExtraDelayFrames = 0 };
    private int extraDelay;
    private int timingRevision;
    private int remoteTimingRevision = -1;
    private int remoteResponseDelay;
    private int remoteDonation;
    private int remoteExtraDelay;
    private int remoteMaxExtraDelay;
    private int remoteFrame = -1;
    private int remoteAdvantage;
    private int lastSampledFrame = -1;
    private long remoteFrameReceivedAt;
    private long lastReceivedAt;
    private long lastSentAt;
    private long lastSyncSentAt;
    private long challengeFirstSentAt = -1;
    private long lastInputSentAt;
    private long lastQualitySentAt;
    private long lastChecksumSentAt;
    private long disconnectedAt;
    private bool interruptionReported;
    private bool statusesChanged;
    private bool acknowledgedInputs;
    private bool excludesRemote;
    private bool excludedByRemote;
    private int committedFrame = -1;
    private uint disconnectMask;
    private int disconnectFloor = -1;
    private int disconnectReadyCut = int.MinValue;
    private double roundTripMilliseconds;
    private long packetsSent;
    private long packetsReceived;
    private long bytesSent;
    private long bytesReceived;
    private long invalidPackets;
    private long stalePackets;

    public PeerProtocol(
        int peerId,
        ulong sessionId,
        ulong configurationId,
        int sendInputSize,
        int receiveInputSize,
        int playerCount,
        SessionOptions options,
        IClock clock,
        ITransport transport,
        int initialFrame = 0
    )
    {
        options.Validate();
        if (
            sessionId == 0
            || initialFrame < 0
            || initialFrame == int.MaxValue
            || playerCount is < 1 or > 64
            || sendInputSize < 0
            || receiveInputSize < 0
            || PacketCodec.HeaderSize
                + PacketCodec.InputHeaderSize
                + playerCount * 5
                + 2L * Math.Max(sendInputSize, receiveInputSize)
                > options.MaxPacketBytes
        )
            throw new ArgumentOutOfRangeException(nameof(sendInputSize), "Invalid peer protocol configuration.");

        this.peerId = peerId;
        this.sessionId = sessionId;
        this.configurationId = configurationId;
        this.sendInputSize = sendInputSize;
        this.receiveInputSize = receiveInputSize;
        this.initialFrame = initialFrame;
        this.options = options;
        this.clock = clock;
        this.transport = transport;
        currentFrame = initialFrame;
        LastQueuedFrame = LastReceivedFrame = LastAcknowledgedFrame = initialFrame - 1;
        localStatuses = Enumerable.Repeat(new ConnectionStatus(false, -1), playerCount).ToArray();
        remoteStatuses = localStatuses.ToArray();
        lastReceivedAt = lastSentAt = clock.NowMilliseconds;
        lastSyncSentAt = lastReceivedAt - options.RetryMilliseconds;
        lastInputSentAt = lastQualitySentAt = lastChecksumSentAt = lastReceivedAt;
    }

    public SessionState State { get; private set; } = SessionState.Synchronizing;
    public int LastReceivedFrame { get; private set; }
    public int LastAcknowledgedFrame { get; private set; }
    public int LastQueuedFrame { get; private set; }
    public int RemoteCommittedFrame { get; private set; } = -1;
    public uint RemoteDisconnectMask { get; private set; }
    public int RemoteDisconnectFloor { get; private set; } = -1;
    public int RemoteDisconnectReadyCut { get; private set; } = int.MinValue;
    public bool CanQueueInput => State != SessionState.Disconnected && pendingInputs.Count < options.InputCapacity;
    public ReadOnlySpan<ConnectionStatus> RemoteStatuses => remoteStatuses;
    public int FramesAhead => timeSync.AverageFrameAdvantage;
    public PeerNetworkStats Stats =>
        new(
            peerId,
            State,
            roundTripMilliseconds,
            FramesAhead,
            pendingInputs.Count,
            LastReceivedFrame,
            LastAcknowledgedFrame,
            packetsSent,
            packetsReceived,
            bytesSent,
            bytesReceived,
            invalidPackets,
            stalePackets
        )
        {
            ResponseDelayFrames = remoteResponseDelay,
            DonationFrames = remoteDonation,
            ExtraDelayFrames = remoteExtraDelay,
            MaxExtraDelayFrames = remoteMaxExtraDelay,
        };

    public void SetSessionProgress(int committedFrame, uint mask = 0, int floor = -1, int readyCut = int.MinValue)
    {
        if (
            committedFrame < this.committedFrame
            || !ValidProgress(committedFrame, mask, floor, readyCut)
            || (mask | disconnectMask) != mask
            || (
                mask == disconnectMask
                && (floor != disconnectFloor || (disconnectReadyCut != int.MinValue && readyCut != disconnectReadyCut))
            )
        )
            throw new ArgumentException(
                "Session progress must preserve committed frames and an existing disconnect agreement."
            );
        statusesChanged |=
            committedFrame != this.committedFrame
            || mask != disconnectMask
            || floor != disconnectFloor
            || readyCut != disconnectReadyCut;
        this.committedFrame = committedFrame;
        disconnectMask = mask;
        disconnectFloor = floor;
        disconnectReadyCut = readyCut;
    }

    public void SetTiming(RollbackTiming value, int extra)
    {
        if (timing == value && extraDelay == extra)
            return;
        timing = value;
        extraDelay = extra;
        timingRevision++;
        statusesChanged = true;
    }

    public void Poll(int frame, ReadOnlySpan<ConnectionStatus> statuses)
    {
        if (frame < initialFrame || statuses.Length != localStatuses.Length)
            throw new ArgumentOutOfRangeException(nameof(frame));
        currentFrame = frame;
        for (int index = 0; index < statuses.Length; index++)
        {
            if (statuses[index].LastFrame < -1)
                throw new ArgumentOutOfRangeException(nameof(statuses));
            statusesChanged |= localStatuses[index] != statuses[index];
            localStatuses[index] = statuses[index];
        }

        long now = clock.NowMilliseconds;
        if (State == SessionState.Disconnected)
        {
            if (
                now - disconnectedAt < options.DisconnectTimeoutMilliseconds
                && now - lastSentAt >= options.RetryMilliseconds
            )
                SendDisconnect();
            return;
        }
        if (now - lastReceivedAt >= options.DisconnectTimeoutMilliseconds)
        {
            Disconnect();
            return;
        }
        if (!interruptionReported && now - lastReceivedAt >= options.DisconnectNotifyMilliseconds)
        {
            interruptionReported = true;
            AddEvent(SessionEventKind.NetworkInterrupted);
        }
        if (State == SessionState.Synchronizing)
        {
            if (now - lastSyncSentAt >= options.RetryMilliseconds)
                SendSynchronization(PacketKind.Synchronize, challenge);
            return;
        }

        if (lastSampledFrame != frame && remoteFrame >= initialFrame)
        {
            timeSync.Record(frame, LocalAdvantage(), remoteAdvantage);
            lastSampledFrame = frame;
        }
        if (
            statusesChanged
            || acknowledgedInputs
            || now - lastInputSentAt >= options.RetryMilliseconds
            || now - lastSentAt >= options.KeepAliveMilliseconds
        )
            SendInputs();
        if (now - lastQualitySentAt >= options.QualityReportMilliseconds)
        {
            lastQualitySentAt = now;
            while (pendingPings.Count > 0 && now - pendingPings.Min >= options.DisconnectTimeoutMilliseconds)
                pendingPings.Remove(pendingPings.Min);
            if (pendingPings.Count < 64)
            {
                pendingPings.Add(now);
                Send(
                    new ProtocolPacket
                    {
                        Kind = PacketKind.Ping,
                        Timestamp = now,
                        Frame = currentFrame,
                        Advantage = LocalAdvantage(),
                    }
                );
            }
        }
        if (now - lastChecksumSentAt >= options.KeepAliveMilliseconds)
        {
            lastChecksumSentAt = now;
            foreach ((int checksumFrame, ulong checksum) in sentChecksums)
                Send(
                    new ProtocolPacket
                    {
                        Kind = PacketKind.Checksum,
                        Frame = checksumFrame,
                        Checksum = checksum,
                    }
                );
        }
    }

    public void QueueInput(int frame, ReadOnlySpan<byte> input)
    {
        if (
            sendInputSize == 0
            || input.Length != sendInputSize
            || frame != (long)LastQueuedFrame + 1
            || frame == int.MaxValue
        )
            throw new ArgumentException(
                "Input must have the configured size and follow the preceding frame.",
                nameof(input)
            );
        if (!CanQueueInput)
            throw new InvalidOperationException("The peer input buffer is full or disconnected.");
        pendingInputs.Add(frame, input.ToArray());
        LastQueuedFrame = frame;
        if (State == SessionState.Running)
            SendInputs();
    }

    public bool TryReceiveInput(out InputPacket input) => receivedInputs.TryDequeue(out input);

    public bool TryGetEvent(out SessionEvent sessionEvent) => events.TryDequeue(out sessionEvent);

    public bool TryReceiveChecksum(out ChecksumPacket checksum) => receivedChecksums.TryDequeue(out checksum);

    public void QueueChecksum(int frame, ulong checksum)
    {
        if (frame < initialFrame || frame == int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(frame));
        if (sentChecksums.TryGetValue(frame, out ulong previous) && previous != checksum)
            throw new InvalidOperationException("A confirmed frame's checksum cannot change.");
        sentChecksums[frame] = checksum;
        while (sentChecksums.Count > 32)
            sentChecksums.Remove(sentChecksums.First().Key);
        if (State == SessionState.Running)
            Send(
                new ProtocolPacket
                {
                    Kind = PacketKind.Checksum,
                    Frame = frame,
                    Checksum = checksum,
                }
            );
    }

    public void Disconnect(bool excludeRemote = false)
    {
        bool upgraded = excludeRemote && !excludesRemote;
        excludesRemote |= excludeRemote;
        if (State == SessionState.Disconnected)
        {
            if (upgraded)
            {
                disconnectedAt = clock.NowMilliseconds;
                SendDisconnect();
            }
            return;
        }
        State = SessionState.Disconnected;
        disconnectedAt = clock.NowMilliseconds;
        AddEvent(SessionEventKind.Disconnected);
        SendDisconnect();
    }

    public void HandlePacket(ReadOnlySpan<byte> data)
    {
        if (data.Length > options.MaxPacketBytes)
        {
            invalidPackets++;
            return;
        }
        if (
            !PacketCodec.TryDecode(
                data,
                sessionId,
                configurationId,
                localStatuses.Length,
                out ProtocolPacket packet,
                out PacketDecodeFailure failure
            )
        )
        {
            if (failure == PacketDecodeFailure.StaleSession)
                stalePackets++;
            else
            {
                invalidPackets++;
                if (failure == PacketDecodeFailure.ConfigurationMismatch)
                    Fail("Peers disagree about the session configuration.");
            }
            return;
        }

        if (State == SessionState.Disconnected)
        {
            if (packet.Kind == PacketKind.Disconnect && packet.ExcludeRemote)
                HandleDisconnect(packet);
            return;
        }

        bool accepted = packet.Kind switch
        {
            PacketKind.Synchronize or PacketKind.SynchronizeReply => HandleSynchronization(packet),
            PacketKind.Inputs when State == SessionState.Running => HandleInputs(packet),
            PacketKind.Ping when State == SessionState.Running => HandlePing(packet),
            PacketKind.Pong when State == SessionState.Running => HandlePong(packet),
            PacketKind.Checksum when State == SessionState.Running => HandleChecksum(packet),
            PacketKind.Disconnect => HandleDisconnect(packet),
            _ => false,
        };
        if (!accepted)
        {
            invalidPackets++;
            return;
        }
        packetsReceived++;
        bytesReceived += data.Length;
        lastReceivedAt = clock.NowMilliseconds;
        if (interruptionReported && State != SessionState.Disconnected)
        {
            interruptionReported = false;
            AddEvent(SessionEventKind.NetworkResumed);
        }
    }

    private bool HandleSynchronization(ProtocolPacket packet)
    {
        if (
            packet.SendInputSize != receiveInputSize
            || packet.ReceiveInputSize != sendInputSize
            || packet.PlayerCount != localStatuses.Length
            || packet.InitialFrame != initialFrame
        )
        {
            Fail("Peers disagree about the input layout or initial frame.");
            return false;
        }
        if (packet.Kind == PacketKind.Synchronize)
        {
            SendSynchronization(PacketKind.SynchronizeReply, packet.Challenge);
            return true;
        }
        if (State != SessionState.Synchronizing || packet.Challenge != challenge)
            return true;
        roundTripMilliseconds = Math.Max(0, clock.NowMilliseconds - challengeFirstSentAt);
        synchronizedRoundTrips++;
        if (synchronizedRoundTrips == options.SynchronizationRoundTrips)
        {
            State = SessionState.Running;
            AddEvent(SessionEventKind.Synchronized);
            SendInputs();
        }
        else
        {
            AddEvent(
                SessionEventKind.Synchronizing,
                detail: $"{synchronizedRoundTrips}/{options.SynchronizationRoundTrips}"
            );
            challenge++;
            challengeFirstSentAt = -1;
            SendSynchronization(PacketKind.Synchronize, challenge);
        }
        return true;
    }

    private bool HandleInputs(ProtocolPacket packet)
    {
        if (
            packet.AcknowledgedFrame < initialFrame - 1
            || packet.AcknowledgedFrame > LastQueuedFrame
            || !ValidRemoteFrame(packet)
            || !ValidateRemoteProgress(packet)
            || packet.Statuses.Any(status => status.LastFrame < -1 || status.LastFrame == int.MaxValue)
        )
            return false;
        byte[][] inputs = [];
        if (packet.InputCount == 0)
        {
            if (packet.InputData.Length != 0 || packet.StartFrame != initialFrame)
                return false;
        }
        else
        {
            if (
                receiveInputSize == 0
                || packet.StartFrame < initialFrame
                || packet.InputCount > FramesPerPacket(receiveInputSize)
                || (long)packet.StartFrame + packet.InputCount - 1 > (long)LastReceivedFrame + options.InputCapacity
                || (long)packet.StartFrame + packet.InputCount - 1 >= int.MaxValue
                || !InputCompression.TryDecode(packet.InputData, packet.InputCount, receiveInputSize, out inputs)
            )
                return false;
            int newInputs = 0;
            for (int index = 0; index < inputs.Length; index++)
            {
                int frame = packet.StartFrame + index;
                if (receivedHistory.TryGetValue(frame, out byte[]? previous))
                {
                    if (!previous.AsSpan().SequenceEqual(inputs[index]))
                    {
                        Fail("A peer changed an input it had already submitted.");
                        return false;
                    }
                }
                else if (frame > LastReceivedFrame)
                    newInputs++;
            }
            if (
                (long)receivedInputs.Count + receivedHistory.Keys.Count(frame => frame > LastReceivedFrame) + newInputs
                > options.InputCapacity
            )
                return false;
        }

        Acknowledge(packet.AcknowledgedFrame);
        MergeStatuses(packet.Statuses);
        UpdateRemoteProgress(packet);
        UpdateRemoteFrame(packet);
        for (int index = 0; index < inputs.Length; index++)
        {
            int frame = packet.StartFrame + index;
            if (frame > LastReceivedFrame)
                receivedHistory.TryAdd(frame, inputs[index]);
        }
        while (receivedHistory.TryGetValue(LastReceivedFrame + 1, out byte[]? next))
        {
            LastReceivedFrame++;
            receivedInputs.Enqueue(new InputPacket(LastReceivedFrame, next.ToArray()));
        }
        while (receivedHistory.Count > 0 && receivedHistory.First().Key < LastReceivedFrame - options.InputCapacity + 1)
            receivedHistory.Remove(receivedHistory.First().Key);
        acknowledgedInputs |= inputs.Length > 0;
        return true;
    }

    private bool HandlePing(ProtocolPacket packet)
    {
        if (packet.Timestamp < 0 || !ValidRemoteFrame(packet))
            return false;
        UpdateRemoteFrame(packet);
        Send(new ProtocolPacket { Kind = PacketKind.Pong, Timestamp = packet.Timestamp });
        return true;
    }

    private bool ValidProgress(int committed, uint mask, int floor, int readyCut)
    {
        uint validMask = localStatuses.Length >= 32 ? uint.MaxValue : (1u << localStatuses.Length) - 1;
        return committed >= -1
            && committed != int.MaxValue
            && (mask & ~validMask) == 0
            && floor >= -1
            && floor <= committed
            && (readyCut == int.MinValue || (readyCut >= floor && readyCut != int.MaxValue))
            && (mask != 0 || (floor == -1 && readyCut == int.MinValue));
    }

    private bool ValidateRemoteProgress(ProtocolPacket packet)
    {
        if (
            packet.CommittedFrame >= packet.Frame
            || !ValidProgress(
                packet.CommittedFrame,
                packet.DisconnectMask,
                packet.DisconnectFloor,
                packet.DisconnectReadyCut
            )
        )
            return false;
        uint combined = packet.DisconnectMask | RemoteDisconnectMask;
        bool sameMask = packet.DisconnectMask == RemoteDisconnectMask;
        bool changedAgreement =
            sameMask
            && (
                packet.DisconnectFloor != RemoteDisconnectFloor
                || (
                    packet.DisconnectReadyCut != int.MinValue
                    && RemoteDisconnectReadyCut != int.MinValue
                    && packet.DisconnectReadyCut != RemoteDisconnectReadyCut
                )
            );
        if ((combined != packet.DisconnectMask && combined != RemoteDisconnectMask) || changedAgreement)
        {
            Fail("A peer changed its disconnect agreement.");
            return false;
        }
        return true;
    }

    private void UpdateRemoteProgress(ProtocolPacket packet)
    {
        RemoteCommittedFrame = Math.Max(RemoteCommittedFrame, packet.CommittedFrame);
        if ((packet.DisconnectMask | RemoteDisconnectMask) != packet.DisconnectMask)
            return;
        if (packet.DisconnectMask != RemoteDisconnectMask)
        {
            RemoteDisconnectMask = packet.DisconnectMask;
            RemoteDisconnectFloor = packet.DisconnectFloor;
            RemoteDisconnectReadyCut = packet.DisconnectReadyCut;
        }
        else if (packet.DisconnectReadyCut != int.MinValue)
            RemoteDisconnectReadyCut = packet.DisconnectReadyCut;
    }

    private bool ValidRemoteFrame(ProtocolPacket packet)
    {
        long maximumAdvantage =
            options.InputCapacity + (long)options.FramesPerSecond * options.DisconnectTimeoutMilliseconds / 1000;
        return packet.Frame >= initialFrame
            && packet.Frame != int.MaxValue
            && Math.Abs((long)packet.Advantage) <= maximumAdvantage
            && packet.TimingRevision >= 0
            && packet.ResponseDelay >= 0
            && packet.ResponseDelay <= options.MaxInputDelay
            && packet.Donation >= 0
            && packet.Donation <= 10000
            && packet.ExtraDelay >= 0
            && packet.ExtraDelay <= packet.MaxExtraDelay
            && packet.MaxExtraDelay >= 0
            && (long)packet.ResponseDelay + packet.MaxExtraDelay <= options.MaxInputDelay;
    }

    private void UpdateRemoteFrame(ProtocolPacket packet)
    {
        if (packet.TimingRevision >= remoteTimingRevision)
        {
            remoteTimingRevision = packet.TimingRevision;
            remoteResponseDelay = packet.ResponseDelay;
            remoteDonation = packet.Donation;
            remoteExtraDelay = packet.ExtraDelay;
            remoteMaxExtraDelay = packet.MaxExtraDelay;
        }
        if (packet.Frame >= remoteFrame)
        {
            remoteFrame = packet.Frame;
            remoteAdvantage = packet.Advantage;
            remoteFrameReceivedAt = clock.NowMilliseconds;
        }
    }

    private bool HandlePong(ProtocolPacket packet)
    {
        if (packet.Timestamp < 0 || packet.Timestamp > clock.NowMilliseconds || !pendingPings.Remove(packet.Timestamp))
            return false;
        roundTripMilliseconds = clock.NowMilliseconds - packet.Timestamp;
        return true;
    }

    private bool HandleChecksum(ProtocolPacket packet)
    {
        if (packet.Frame < initialFrame || packet.Frame > (long)currentFrame + options.InputCapacity)
            return false;
        if (receivedChecksumHistory.TryGetValue(packet.Frame, out ulong previous))
        {
            if (previous != packet.Checksum)
            {
                Fail("A peer changed a confirmed checksum.");
                return false;
            }
            return true;
        }
        receivedChecksumHistory[packet.Frame] = packet.Checksum;
        while (receivedChecksumHistory.Count > 32)
            receivedChecksumHistory.Remove(receivedChecksumHistory.First().Key);
        if (receivedChecksums.Count == 32)
            receivedChecksums.Dequeue();
        receivedChecksums.Enqueue(new ChecksumPacket(packet.Frame, packet.Checksum));
        return true;
    }

    private bool HandleDisconnect(ProtocolPacket packet)
    {
        if (packet.ExcludeRemote && !excludedByRemote)
        {
            excludedByRemote = true;
            AddEvent(SessionEventKind.Excluded);
        }
        Disconnect();
        return true;
    }

    private void SendDisconnect() =>
        Send(new ProtocolPacket { Kind = PacketKind.Disconnect, ExcludeRemote = excludesRemote });

    private void MergeStatuses(ReadOnlySpan<ConnectionStatus> statuses)
    {
        for (int index = 0; index < statuses.Length; index++)
        {
            ConnectionStatus old = remoteStatuses[index];
            ConnectionStatus incoming = statuses[index];
            if (incoming.Disconnected)
                remoteStatuses[index] = new ConnectionStatus(
                    true,
                    old.Disconnected ? Math.Min(old.LastFrame, incoming.LastFrame) : incoming.LastFrame
                );
            else if (!old.Disconnected)
                remoteStatuses[index] = new ConnectionStatus(false, Math.Max(old.LastFrame, incoming.LastFrame));
        }
    }

    private void Acknowledge(int frame)
    {
        if (frame <= LastAcknowledgedFrame)
            return;
        LastAcknowledgedFrame = frame;
        while (pendingInputs.Count > 0 && pendingInputs.First().Key <= frame)
            pendingInputs.Remove(pendingInputs.First().Key);
    }

    private void SendInputs()
    {
        lastInputSentAt = clock.NowMilliseconds;
        acknowledgedInputs = statusesChanged = false;
        if (pendingInputs.Count == 0)
        {
            SendInputChunk(initialFrame, [], 0);
            return;
        }
        int framesPerPacket = FramesPerPacket(sendInputSize);
        byte[][] frames = pendingInputs.Values.ToArray();
        int firstFrame = pendingInputs.First().Key;
        for (int end = frames.Length; end > 0; )
        {
            int count = Math.Min(end, framesPerPacket);
            int start = end - count;
            byte[] encoded = InputCompression.Encode(frames, start, count, sendInputSize);
            SendInputChunk(firstFrame + start, encoded, count);
            end = start;
        }
    }

    private void SendInputChunk(int frame, byte[] data, int count)
    {
        Send(
            new ProtocolPacket
            {
                Kind = PacketKind.Inputs,
                AcknowledgedFrame = LastReceivedFrame,
                Frame = currentFrame,
                Advantage = LocalAdvantage(),
                CommittedFrame = committedFrame,
                DisconnectMask = disconnectMask,
                DisconnectFloor = disconnectFloor,
                DisconnectReadyCut = disconnectReadyCut,
                Statuses = localStatuses,
                StartFrame = frame,
                InputCount = count,
                InputData = data,
            }
        );
    }

    private int FramesPerPacket(int inputSize)
    {
        int bodyBytes =
            options.MaxPacketBytes - PacketCodec.HeaderSize - PacketCodec.InputHeaderSize - localStatuses.Length * 5;
        return Math.Max(1, bodyBytes / (inputSize * 2));
    }

    private int LocalAdvantage()
    {
        if (remoteFrame < initialFrame)
            return 0;
        double elapsed = Math.Max(0, clock.NowMilliseconds - remoteFrameReceivedAt);
        double projected =
            remoteFrame
            + remoteResponseDelay
            + remoteExtraDelay
            - remoteDonation
            + (elapsed + roundTripMilliseconds / 2) * options.FramesPerSecond / 1000;
        double local = currentFrame + timing.DelayFrames + extraDelay - timing.DonationFrames;
        return (int)Math.Clamp(projected - local, -options.InputCapacity, options.InputCapacity);
    }

    private void SendSynchronization(PacketKind kind, uint token)
    {
        if (kind == PacketKind.Synchronize)
        {
            lastSyncSentAt = clock.NowMilliseconds;
            if (challengeFirstSentAt < 0)
                challengeFirstSentAt = lastSyncSentAt;
        }
        Send(
            new ProtocolPacket
            {
                Kind = kind,
                Challenge = token,
                SendInputSize = sendInputSize,
                ReceiveInputSize = receiveInputSize,
                PlayerCount = localStatuses.Length,
                InitialFrame = initialFrame,
            }
        );
    }

    private void Send(ProtocolPacket packet)
    {
        packet.TimingRevision = timingRevision;
        packet.ResponseDelay = timing.DelayFrames;
        packet.Donation = timing.DonationFrames;
        packet.ExtraDelay = extraDelay;
        packet.MaxExtraDelay = timing.MaxExtraDelayFrames;
        byte[] encoded = PacketCodec.Encode(sessionId, configurationId, packet);
        transport.Send(peerId, encoded);
        lastSentAt = clock.NowMilliseconds;
        packetsSent++;
        bytesSent += encoded.Length;
    }

    private void Fail(string detail)
    {
        AddEvent(SessionEventKind.ProtocolError, detail: detail);
        Disconnect();
    }

    private void AddEvent(SessionEventKind kind, string? detail = null)
    {
        if (events.Count == 64)
            events.Dequeue();
        events.Enqueue(new SessionEvent(kind, peerId, Detail: detail));
    }
}
