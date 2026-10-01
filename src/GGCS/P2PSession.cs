using GGCS.Core;
using GGCS.Protocol;

namespace GGCS;

public sealed class P2PSession<TInput, TState>
    where TInput : unmanaged
{
    private readonly int localPeerId;
    private readonly ulong sessionId;
    private readonly SessionOptions options;
    private readonly IClock clock;
    private readonly ITransport transport;
    private readonly SessionWire<TInput> wire;
    private readonly RollbackEngine<TInput, TState> engine;
    private readonly Dictionary<int, PeerProtocol> peers = new();
    private readonly Dictionary<int, PeerProtocol> observers = new();
    private readonly Dictionary<int, PeerProtocol> spectators = new();
    private readonly Dictionary<int, int[]> peerHandles;
    private readonly Dictionary<int, DelayedInputQueue<TInput>> delays = new();
    private readonly Dictionary<int, SortedDictionary<int, ulong>> remoteChecksums = new();
    private readonly ConnectionStatus[] statuses;
    private readonly int[] localHandles;
    private readonly SessionEvents events = new();
    private int submittedFrame = -1;
    private int nextOutgoingFrame;
    private int lastChecksumFrame = -1;
    private bool stopped;
    private bool hasSynchronized;
    private uint disconnectMask;
    private uint committedDisconnectMask;
    private int disconnectFloor = -1;
    private int disconnectReadyCut = int.MinValue;
    private readonly Dictionary<int, long> missingObserverStreams = new();
    private bool HasPendingDisconnect => (disconnectMask & ~committedDisconnectMask) != 0;

    public P2PSession(
        ulong sessionId,
        int localPeerId,
        IReadOnlyList<Player> players,
        IRollbackGame<TInput, TState> game,
        IInputCodec<TInput> codec,
        ITransport transport,
        SessionOptions? options = null,
        IClock? clock = null,
        Func<TInput, int, TInput>? predictor = null,
        string inputSchema = ""
    )
    {
        if (sessionId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(sessionId),
                "Use a fresh, nonzero ID for each session generation."
            );
        if (localPeerId < 0)
            throw new ArgumentOutOfRangeException(nameof(localPeerId));
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(transport);
        this.sessionId = sessionId;
        this.localPeerId = localPeerId;
        this.transport = transport;
        this.options = options ?? new();
        this.clock = clock ?? new MonotonicClock();
        wire = new(players, codec, this.options, inputSchema);
        engine = new(wire.Players.Length, this.options, game, predictor);
        statuses = Enumerable.Repeat(new ConnectionStatus(false, -1), wire.Players.Length).ToArray();
        peerHandles = wire
            .Players.GroupBy(p => p.PeerId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Handle).ToArray());
        localHandles = peerHandles.GetValueOrDefault(localPeerId) ?? [];
        foreach (int handle in localHandles)
            delays.Add(handle, new(this.options.InputDelay, this.options.MaxInputDelay));
        foreach (var (peerId, handles) in peerHandles)
        {
            if (peerId == localPeerId)
                continue;
            peers.Add(
                peerId,
                CreatePeer(peerId, localHandles.Length * wire.InputSize, handles.Length * wire.InputSize)
            );
            remoteChecksums.Add(peerId, new());
        }
        hasSynchronized = peers.Count == 0;
    }

    public int CurrentFrame => engine.CurrentFrame;
    public int ConfirmedFrame => engine.ConfirmedFrame;
    public IReadOnlyList<int> LocalPlayerHandles => Array.AsReadOnly(localHandles);
    public bool IsInputObserver => localHandles.Length == 0;
    public SessionState State =>
        stopped ? SessionState.Disconnected
        : peers.Values.Any(p => p.State == SessionState.Synchronizing) ? SessionState.Synchronizing
        : SessionState.Running;
    public int FramesAhead =>
        peers.Values.Where(p => p.State == SessionState.Running).Select(p => p.FramesAhead).DefaultIfEmpty().Max();
    public double RecommendedFrameDurationMultiplier => FramesAhead >= 3 ? 1.1 : 1.0;
    public int PredictionDepth =>
        Enumerable
            .Range(0, statuses.Length)
            .Where(p => !statuses[p].Disconnected)
            .Select(p => Math.Max(0, CurrentFrame - statuses[p].LastFrame - 1))
            .DefaultIfEmpty()
            .Max();
    public long TotalResimulatedFrames { get; private set; }
    public int LargestRollback { get; private set; }

    public bool TryGetEvent(out SessionEvent item) => events.TryGet(out item);

    public bool TryGetChecksum(int stateFrame, out ulong checksum) => engine.TryGetChecksum(stateFrame, out checksum);

    public bool TryGetConfirmedState(int stateFrame, out SavedState<TState> state)
    {
        state = default;
        return stateFrame <= ConfirmedFrame + 1 && engine.TryGetState(stateFrame, out state);
    }

    public bool TryGetConfirmedInputs(int frame, out PlayerInput<TInput>[] inputs)
    {
        inputs = [];
        return frame <= ConfirmedFrame && engine.TryGetInputs(frame, out inputs);
    }

    public PeerNetworkStats GetNetworkStats(int peerId) => FindPeer(peerId).Stats;

    public void SetInputDelay(int playerHandle, int delay)
    {
        if (!delays.TryGetValue(playerHandle, out var queue))
            throw new ArgumentException("Only a local player's input delay can be changed.", nameof(playerHandle));
        queue.SetDelay(delay);
    }

    public void AddInputObserver(int peerId)
    {
        EnsureNewPeer(peerId);
        if (CurrentFrame != 0 || submittedFrame >= 0 || IsInputObserver)
            throw new InvalidOperationException("Register raw-input observers on playing machines before frame zero.");
        observers.Add(peerId, CreatePeer(peerId, localHandles.Length * wire.InputSize, 0));
    }

    public void AddSpectator(int peerId, int startFrame = 0)
    {
        EnsureNewPeer(peerId);
        if (startFrame < Math.Max(0, CurrentFrame - options.HistoryFrames) || startFrame > ConfirmedFrame + 1)
            throw new ArgumentOutOfRangeException(
                nameof(startFrame),
                "The spectator needs a checkpoint within retained history."
            );
        spectators.Add(peerId, CreatePeer(peerId, wire.SpectatorInputSize, 0, startFrame));
    }

    public void DisconnectPeer(int peerId)
    {
        if (observers.Remove(peerId, out var observer))
            observer.Disconnect();
        else if (spectators.Remove(peerId, out var spectator))
            spectator.Disconnect();
        else if (peers.ContainsKey(peerId))
        {
            if (IsInputObserver)
                Close();
            else
                DisconnectPlayingPeer(peerId);
        }
        else
            throw new ArgumentException("The peer is not part of this session.", nameof(peerId));
    }

    public void Close()
    {
        if (stopped)
            return;
        stopped = true;
        foreach (var peer in AllPeers())
            peer.Disconnect();
    }

    public void Poll()
    {
        if (stopped)
            return;
        for (int i = 0; i < options.MaxPacketsPerPoll && transport.TryReceive(out var datagram); i++)
        {
            if (TryFindPeer(datagram.PeerId, out var peer))
                peer.HandlePacket(datagram.Data.Span);
        }
        foreach (var (peerId, peer) in peers)
        {
            SetPeerProgress(peer);
            peer.Poll(CurrentFrame, statuses);
            ReceivePlayerInputs(peerId, peer);
            ReceiveEvents(peerId, peer, true);
            if (stopped)
                return;
            var checksums = remoteChecksums[peerId];
            while (peer.TryReceiveChecksum(out var checksum))
            {
                checksums[checksum.Frame] = checksum.Checksum;
                while (checksums.Count > options.HistoryFrames)
                    checksums.Remove(checksums.First().Key);
            }
        }
        foreach (var (peerId, peer) in observers.Concat(spectators).ToArray())
        {
            SetPeerProgress(peer);
            peer.Poll(CurrentFrame, statuses);
            ReceiveEvents(peerId, peer, false);
        }
        if (stopped)
            return;
        if (peers.Values.All(peer => peer.State == SessionState.Running))
            hasSynchronized = true;
        UpdateDisconnectAgreement();
        if (stopped)
            return;
        RepairAndConfirm();
        PublishSpectators();
        CheckDesyncs();
        foreach (var peer in AllPeers())
            SetPeerProgress(peer);
    }

    public AdvanceStatus AdvanceFrame(ReadOnlySpan<TInput> localInputs)
    {
        Poll();
        if (State == SessionState.Disconnected)
            return AdvanceStatus.Disconnected;
        if (State == SessionState.Synchronizing)
            return AdvanceStatus.Synchronizing;
        if (HasPendingDisconnect)
            return AdvanceStatus.DisconnectAgreement;
        if (IsInputObserver && missingObserverStreams.Count > 0)
            return AdvanceStatus.WaitingForInput;
        if (localInputs.Length != localHandles.Length)
            throw new ArgumentException(
                "Supply one input for each local handle, in LocalPlayerHandles order.",
                nameof(localInputs)
            );
        if (submittedFrame != CurrentFrame)
        {
            if (peers.Values.Any(p => p.State == SessionState.Running && !p.CanQueueInput))
                return AdvanceStatus.InputBufferFull;
            for (int i = 0; i < localHandles.Length; i++)
            {
                int handle = localHandles[i];
                foreach (var input in delays[handle].Submit(CurrentFrame, localInputs[i]))
                {
                    engine.AddInput(handle, input.Frame, input.Input);
                    statuses[handle] = new(false, input.Frame);
                }
            }
            submittedFrame = CurrentFrame;
        }
        SendReadyLocalInputs();
        RepairAndConfirm();
        if (IsInputObserver && GloballyKnownFrame() < CurrentFrame)
            return AdvanceStatus.WaitingForInput;
        if (!engine.Advance())
            return AdvanceStatus.PredictionLimit;
        RepairAndConfirm();
        PublishSpectators();
        CheckDesyncs();
        foreach (var peer in AllPeers())
        {
            SetPeerProgress(peer);
            peer.Poll(CurrentFrame, statuses);
        }
        return AdvanceStatus.Advanced;
    }

    private PeerProtocol CreatePeer(int peerId, int sendSize, int receiveSize, int startFrame = 0) =>
        new(
            peerId,
            sessionId,
            wire.ConfigurationId,
            sendSize,
            receiveSize,
            wire.Players.Length,
            options,
            clock,
            transport,
            startFrame
        );

    private IEnumerable<PeerProtocol> AllPeers() => peers.Values.Concat(observers.Values).Concat(spectators.Values);

    private bool TryFindPeer(int peerId, out PeerProtocol peer) =>
        peers.TryGetValue(peerId, out peer!)
        || observers.TryGetValue(peerId, out peer!)
        || spectators.TryGetValue(peerId, out peer!);

    private PeerProtocol FindPeer(int peerId) =>
        TryFindPeer(peerId, out var peer)
            ? peer
            : throw new ArgumentException("The peer is not part of this session.", nameof(peerId));

    private void EnsureNewPeer(int peerId)
    {
        if (stopped || peerId < 0 || peerId == localPeerId || TryFindPeer(peerId, out _))
            throw new ArgumentException(
                "Expected a new, nonnegative peer ID distinct from the local peer.",
                nameof(peerId)
            );
    }

    private void ReceivePlayerInputs(int peerId, PeerProtocol peer)
    {
        var handles = peerHandles[peerId];
        while (peer.TryReceiveInput(out var packet))
        {
            TInput[] inputs;
            try
            {
                inputs = wire.Decode(packet.Data, handles.Length);
            }
            catch (Exception exception)
                when (exception is ArgumentException or FormatException or OverflowException or InvalidDataException)
            {
                Fail(peerId, $"Input codec rejected frame {packet.Frame}: {exception.Message}");
                return;
            }
            for (int i = 0; i < handles.Length; i++)
            {
                int handle = handles[i];
                if (statuses[handle].Disconnected)
                    continue;
                if (
                    (long)packet.Frame
                    >= Math.Max(0, CurrentFrame - options.HistoryFrames) + (long)options.InputCapacity
                )
                {
                    Fail(peerId, "Remote input exceeded the retained frame window.");
                    return;
                }
                engine.AddInput(handle, packet.Frame, inputs[i]);
                statuses[handle] = new(false, packet.Frame);
            }
        }
    }

    private void ReceiveEvents(int peerId, PeerProtocol peer, bool playing)
    {
        while (peer.TryGetEvent(out var item))
        {
            events.Add(item);
            if (item.Kind is SessionEventKind.Excluded or SessionEventKind.ProtocolError)
            {
                if (playing)
                    Close();
                else
                {
                    peer.Disconnect();
                    observers.Remove(peerId);
                    spectators.Remove(peerId);
                }
                return;
            }
            if (item.Kind != SessionEventKind.Disconnected)
                continue;
            if (playing && !hasSynchronized)
            {
                Fail(peerId, "A required peer failed to synchronize.");
                return;
            }
            if (playing && IsInputObserver)
            {
                if (peerHandles[peerId].Any(handle => !statuses[handle].Disconnected))
                    missingObserverStreams.TryAdd(peerId, clock.NowMilliseconds);
            }
            else if (playing)
                DisconnectPlayingPeer(peerId);
            else
            {
                observers.Remove(peerId);
                spectators.Remove(peerId);
            }
        }
    }

    private void SendReadyLocalInputs()
    {
        if (localHandles.Length == 0)
            return;
        int readyThrough = localHandles.Min(engine.LastInputFrame);
        var inputs = new TInput[localHandles.Length];
        while (nextOutgoingFrame <= readyThrough)
        {
            if (peers.Values.Any(p => p.State == SessionState.Running && !p.CanQueueInput))
                return;
            for (int i = 0; i < localHandles.Length; i++)
            {
                if (!engine.TryGetInput(localHandles[i], nextOutgoingFrame, out inputs[i]))
                    throw new InvalidOperationException("Local input history was overwritten before sending.");
            }
            var bytes = wire.Encode(inputs);
            foreach (var peer in peers.Values)
                if (peer.State == SessionState.Running)
                    peer.QueueInput(nextOutgoingFrame, bytes);
            foreach (var (peerId, peer) in observers.ToArray())
            {
                if (!peer.CanQueueInput)
                    DropObserver(peerId, peer, "Input observer exceeded retained history.");
                else
                    peer.QueueInput(nextOutgoingFrame, bytes);
            }
            nextOutgoingFrame++;
        }
    }

    private void SetPeerProgress(PeerProtocol peer) =>
        peer.SetSessionProgress(ConfirmedFrame, disconnectMask, disconnectFloor, disconnectReadyCut);

    private uint MaskForPeer(int peerId)
    {
        uint mask = 0;
        foreach (int handle in peerHandles[peerId])
            mask |= 1u << handle;
        return mask;
    }

    private bool IsRemovingPeer(int peerId) => (disconnectMask & MaskForPeer(peerId)) != 0;

    private void DisconnectPlayingPeer(int peerId)
    {
        BeginDisconnect(disconnectMask | MaskForPeer(peerId));
        peers[peerId].Disconnect(excludeRemote: true);
    }

    private void BeginDisconnect(uint mask)
    {
        if ((mask | disconnectMask) == disconnectMask)
            return;
        foreach (int peerId in peerHandles.Keys)
        {
            uint owned = MaskForPeer(peerId);
            if ((mask & owned) != 0 && (mask & owned) != owned)
            {
                Fail(peerId, "A disconnect must remove all players owned by a machine.");
                return;
            }
        }
        if (localHandles.Any(handle => (mask & (1u << handle)) != 0))
        {
            Fail(localPeerId, "Another participant excluded this machine from the session.");
            return;
        }
        disconnectMask |= mask;
        disconnectFloor = ConfirmedFrame;
        disconnectReadyCut = int.MinValue;
        if (!IsInputObserver)
            foreach (var (peerId, peer) in peers)
                if (IsRemovingPeer(peerId))
                    peer.Disconnect(excludeRemote: true);
    }

    private void ApplyDisconnect(int handle, int cutoff)
    {
        if (statuses[handle].Disconnected)
        {
            if (statuses[handle].LastFrame != cutoff)
                Fail(wire.Players[handle].PeerId, "Peers disagree about a committed disconnect cutoff.");
            return;
        }
        if (
            wire.Players[handle].PeerId == localPeerId
            || cutoff < ConfirmedFrame
            || cutoff > statuses[handle].LastFrame
        )
        {
            Fail(
                wire.Players[handle].PeerId,
                "The agreed disconnect cannot be applied to retained inputs; a new checkpoint is required."
            );
            return;
        }
        statuses[handle] = new(true, cutoff);
        committedDisconnectMask |= 1u << handle;
        engine.DisconnectPlayer(handle, cutoff);
    }

    private void UpdateDisconnectAgreement()
    {
        uint mask = disconnectMask;
        foreach (var (peerId, peer) in peers)
            if (!IsRemovingPeer(peerId))
                mask |= peer.RemoteDisconnectMask;
        BeginDisconnect(mask);
        if (stopped)
            return;

        foreach (var (peerId, peer) in peers)
        {
            if (IsRemovingPeer(peerId))
                continue;
            for (int handle = 0; handle < statuses.Length; handle++)
            {
                var remote = peer.RemoteStatuses[handle];
                if (remote.Disconnected)
                    ApplyDisconnect(handle, remote.LastFrame);
                if (stopped)
                    return;
            }
        }

        if (!IsInputObserver && HasPendingDisconnect)
        {
            var remaining = peers.Where(pair => !IsRemovingPeer(pair.Key)).Select(pair => pair.Value).ToArray();
            if (
                remaining.All(peer => peer.State == SessionState.Running && peer.RemoteDisconnectMask == disconnectMask)
            )
            {
                int cutoff = remaining.Select(peer => peer.RemoteDisconnectFloor).Append(disconnectFloor).Max();
                disconnectReadyCut = cutoff;
                if (remaining.All(peer => peer.RemoteDisconnectReadyCut == cutoff))
                {
                    for (int handle = 0; handle < statuses.Length; handle++)
                        if ((disconnectMask & (1u << handle)) != 0 && !statuses[handle].Disconnected)
                            ApplyDisconnect(handle, cutoff);
                }
            }
        }

        if (IsInputObserver)
        {
            foreach (var (peerId, lostAt) in missingObserverStreams.ToArray())
            {
                if (peerHandles[peerId].All(handle => statuses[handle].Disconnected))
                    missingObserverStreams.Remove(peerId);
                else if (clock.NowMilliseconds - lostAt >= options.DisconnectTimeoutMilliseconds)
                    Fail(peerId, "The observer lost a player input stream; a new checkpoint is required.");
            }
            foreach (var (peerId, peer) in peers)
                if (peerHandles[peerId].All(handle => statuses[handle].Disconnected))
                    peer.Disconnect();
        }
    }

    private int GloballyKnownFrame()
    {
        int confirmed = int.MaxValue;
        for (int handle = 0; handle < statuses.Length; handle++)
        {
            var status = statuses[handle];
            if (!status.Disconnected)
                confirmed = Math.Min(confirmed, status.LastFrame);
            foreach (var peer in peers.Values)
            {
                if (peer.State == SessionState.Disconnected)
                    continue;
                var remote = peer.RemoteStatuses[handle];
                if (!status.Disconnected || !remote.Disconnected || remote.LastFrame != status.LastFrame)
                    confirmed = Math.Min(confirmed, Math.Min(status.LastFrame, remote.LastFrame));
            }
        }
        if (IsInputObserver)
            foreach (var (peerId, peer) in peers)
                if (!IsRemovingPeer(peerId))
                    confirmed = Math.Min(confirmed, peer.RemoteCommittedFrame);
        return confirmed == int.MaxValue ? CurrentFrame - 1 : confirmed;
    }

    private void RepairAndConfirm()
    {
        int replayed = engine.Repair();
        TotalResimulatedFrames += replayed;
        LargestRollback = Math.Max(LargestRollback, replayed);
        if (HasPendingDisconnect || (IsInputObserver && missingObserverStreams.Count > 0))
            return;
        int confirmed = Math.Min(CurrentFrame - 1, GloballyKnownFrame());
        if (confirmed > ConfirmedFrame)
            engine.SetConfirmedFrame(confirmed);
    }

    private void PublishSpectators()
    {
        foreach (var (peerId, peer) in spectators.ToArray())
        {
            while (peer.LastQueuedFrame < ConfirmedFrame)
            {
                int frame = peer.LastQueuedFrame + 1;
                if (!peer.CanQueueInput || !engine.TryGetInputs(frame, out var inputs))
                {
                    DropObserver(peerId, peer, "Spectator exceeded retained history; a new checkpoint is required.");
                    break;
                }
                peer.QueueInput(frame, wire.EncodeSpectator(inputs));
            }
        }
    }

    private void DropObserver(int peerId, PeerProtocol peer, string reason)
    {
        peer.Disconnect();
        observers.Remove(peerId);
        spectators.Remove(peerId);
        events.Add(new(SessionEventKind.SpectatorTooFarBehind, peerId, CurrentFrame, reason));
    }

    private void CheckDesyncs()
    {
        if (options.ChecksumInterval == 0)
            return;
        int latest = ConfirmedFrame + 1;
        int frame = latest - latest % options.ChecksumInterval;
        if (frame > lastChecksumFrame && engine.TryGetChecksum(frame, out ulong hash))
        {
            foreach (var peer in peers.Values)
                if (peer.State == SessionState.Running)
                    peer.QueueChecksum(frame, hash);
            lastChecksumFrame = frame;
        }
        foreach (var (peerId, checksums) in remoteChecksums)
        {
            foreach (var (remoteFrame, remoteHash) in checksums.ToArray())
            {
                if (remoteFrame > latest)
                    continue;
                if (!engine.TryGetChecksum(remoteFrame, out ulong localHash))
                {
                    if (remoteFrame < CurrentFrame - options.HistoryFrames)
                        checksums.Remove(remoteFrame);
                    continue;
                }
                if (remoteHash != localHash)
                    events.Add(
                        new(
                            SessionEventKind.DesyncDetected,
                            peerId,
                            remoteFrame,
                            $"Local {localHash:x16}; remote {remoteHash:x16}."
                        )
                    );
                checksums.Remove(remoteFrame);
            }
        }
    }

    private void Fail(int peerId, string reason)
    {
        events.Add(new(SessionEventKind.ProtocolError, peerId, CurrentFrame, reason));
        Close();
    }
}
