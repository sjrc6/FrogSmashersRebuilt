using GGCS.Protocol;

namespace GGCS;

public sealed partial class P2PSession<TInput, TState>
    where TInput : unmanaged
{
    public bool CanPreparePeer => participation != null && !stopped && !HasPendingDisconnect;

    public SessionDisconnectState CaptureDisconnects()
    {
        if (!CanPreparePeer)
            throw new InvalidOperationException("Complete disconnect agreement before admitting a machine.");
        return new(
            disconnectFloor,
            disconnectReadyCut,
            statuses.Select(status => status.Disconnected ? status.LastFrame : int.MaxValue).ToArray()
        );
    }

    private void RestoreDisconnects(SessionDisconnectState state)
    {
        if (
            participation == null
            || state.Cutoffs == null
            || state.Cutoffs.Length != statuses.Length
            || state.Cutoffs.Any(cutoff => cutoff != int.MaxValue && (cutoff < -1 || cutoff >= initialFrame))
            || state.Cutoffs.Any(cutoff => cutoff != int.MaxValue)
                && (
                    state.Floor < -1
                    || state.ReadyCut < state.Floor
                    || state.ReadyCut >= initialFrame
                    || state.Cutoffs.Any(cutoff => cutoff != int.MaxValue && cutoff > state.ReadyCut)
                )
            || state.Cutoffs.All(cutoff => cutoff == int.MaxValue)
                && (state.Floor != -1 || state.ReadyCut != int.MinValue)
        )
            throw new ArgumentException("Invalid participation checkpoint.", nameof(state));
        for (int handle = 0; handle < statuses.Length; handle++)
        {
            int cutoff = state.Cutoffs[handle];
            if (cutoff == int.MaxValue)
                continue;
            if (localHandles.Contains(handle) || handle == participation.AuthorityHandle)
                throw new ArgumentException("An admitted machine and the authority must be connected.", nameof(state));
            statuses[handle] = new(true, cutoff);
            engine.SeedDisconnect(handle, cutoff);
            disconnectMask |= 1u << handle;
        }
        committedDisconnectMask = disconnectMask;
        disconnectFloor = state.Floor;
        disconnectReadyCut = state.ReadyCut;
    }

    private IEnumerable<PeerProtocol> RequiredPeerLinks() =>
        peers.Where(pair => PeerParticipates(pair.Key, CurrentFrame)).Select(pair => pair.Value);

    private bool PeerParticipates(int peer, int frame) =>
        participation == null || peerHandles[peer].Any(handle => engine.Participates(handle, frame));

    public bool ConnectPeer(int peer, int firstFrame)
    {
        if (participation == null)
            throw new InvalidOperationException(
                "A running session needs an authoritative participation stream to admit peers."
            );
        if (
            !peerHandles.TryGetValue(peer, out var handles)
            || firstFrame < Math.Max(initialFrame, CurrentFrame - options.HistoryFrames)
            || firstFrame > CurrentFrame
            || handles.Any(handle => statuses[handle].Disconnected && firstFrame <= statuses[handle].LastFrame)
        )
            throw new ArgumentOutOfRangeException(nameof(firstFrame));
        if (peers.TryGetValue(peer, out var previous))
        {
            if (previous.InitialFrame == firstFrame && previous.State != SessionState.Disconnected)
                return true;
            if (PeerParticipates(peer, CurrentFrame) || PeerParticipates(peer, ConfirmedFrame + 1))
                return false;
            previous.Disconnect();
            peers.Remove(peer);
        }
        EnsureNewPeer(peer);
        foreach (int handle in handles)
        {
            engine.StartInput(handle, firstFrame);
            statuses[handle] = new(false, engine.LastInputFrame(handle));
            disconnectMask &= ~(1u << handle);
            committedDisconnectMask &= ~(1u << handle);
            foreach (var link in AllPeers())
                link.RestartInput(handle, firstFrame);
        }
        if (disconnectMask == 0)
        {
            disconnectFloor = -1;
            disconnectReadyCut = int.MinValue;
        }
        peers.Add(
            peer,
            CreatePeer(peer, localHandles.Length * wire.InputSize, handles.Length * wire.InputSize, firstFrame)
        );
        remoteChecksums[peer] = new();
        verifiedStateFrames.Remove(peer);
        return true;
    }

    public void RemoveInactivePeer(int peer)
    {
        if (participation == null || PeerParticipates(peer, ConfirmedFrame + 1) || PeerParticipates(peer, CurrentFrame))
            throw new InvalidOperationException("Only a confirmed inactive participant can release its connection.");
        if (peers.Remove(peer, out var link))
            link.Disconnect();
        remoteChecksums.Remove(peer);
        verifiedStateFrames.Remove(peer);
    }

    private int KnownParticipationFrame()
    {
        int known = ConfirmedFrame;
        for (int frame = ConfirmedFrame + 1; frame <= CurrentFrame; frame++)
        {
            if (!engine.HasConfirmedInputs(frame))
                break;
            bool received = true;
            for (int handle = 0; handle < statuses.Length && received; handle++)
            {
                if (!engine.Participates(handle, frame) || statuses[handle].Disconnected)
                    continue;
                foreach (int peer in peerHandles.Keys)
                {
                    if (peer == localPeerId || IsRemovingPeer(peer) || !PeerParticipates(peer, frame))
                        continue;
                    if (
                        !peers.TryGetValue(peer, out var link)
                        || link.State != SessionState.Running
                        || link.RemoteStatuses[handle].LastFrame < frame
                    )
                    {
                        received = false;
                        break;
                    }
                }
            }
            if (!received)
                break;
            known = frame;
        }
        return known;
    }
}
