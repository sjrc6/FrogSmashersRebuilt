using System.Security.Cryptography;

namespace FrogSmashers.Network;

internal sealed partial class MeshLobby
{
    private sealed class Admission(int peer, string id, long created, int[] required)
    {
        public int Peer { get; } = peer;
        public string Id { get; } = id;
        public long Created { get; } = created;
        public int[] Required { get; } = required;
        public HashSet<int> Ready { get; } = new();
    }

    private sealed class Bootstrap(Control offer)
    {
        public Control Offer { get; } = offer;
        public Dictionary<int, byte[]> Chunks { get; } = new();
    }

    private sealed class PeerPreparation(Control offer)
    {
        public Control Offer { get; } = offer;
        public bool Started { get; set; }
    }

    private readonly Dictionary<int, Admission> admissions = new();
    private readonly HashSet<int> preparedPeers = [0];
    private readonly HashSet<int> passivePeers = new();
    private readonly HashSet<int> simulationReadyPeers = new();
    private readonly Dictionary<int, PeerPreparation> preparedLinks = new();
    private Bootstrap? incomingBootstrap;
    private Control? localPreparation;

    private void UpdateParticipation()
    {
        if (simulation == null || Starting || checkpoint != null || matchRequested)
            return;
        InstallBootstrap();
        if (LiveSession is not { } session)
            return;
        foreach (var (peer, preparation) in preparedLinks.ToArray())
        {
            if (!session.Spectating && !preparation.Started)
            {
                preparation.Started = session.ConnectPeer(peer, preparation.Offer.Tick);
            }
            if (
                preparation.Started
                && session.PeerStats.Any(stats => stats.PeerId == peer && stats.State == GGCS.SessionState.Running)
            )
            {
                SendPeerControl(
                    0,
                    new Control
                    {
                        Kind = ControlKind.Prepared,
                        Nonce = nonce,
                        Generation = Generation,
                        Peer = peer,
                        Transaction = preparation.Offer.Transaction,
                    }
                );
                preparedLinks.Remove(peer);
            }
        }
        if (!IsHost)
        {
            if (phase == LobbyPhase.AwaitingSimulation)
                session.CatchUp();
            if (
                localPreparation is { } preparation
                && session.PeerStats.All(stats => stats.State == GGCS.SessionState.Running)
            )
            {
                session.CatchUp();
                if (session.CaughtUp)
                {
                    SendPeerControl(
                        0,
                        new Control
                        {
                            Kind = ControlKind.Prepared,
                            Nonce = nonce,
                            Generation = Generation,
                            Peer = LocalPeer,
                            Transaction = preparation.Transaction,
                        }
                    );
                    localPreparation = null;
                }
            }
            if (session.Spectating || simulation.Membership.Humans(LocalPeer).Length > 0)
                phase = LobbyPhase.Lobby;
            return;
        }
        foreach (var admission in admissions.Values.ToArray())
        {
            if (Now - admission.Created > 10000)
            {
                Reject(peerAddresses[admission.Peer], "CONNECTION TIMED OUT", clientNonces[admission.Peer]);
                RemovePeer(peerAddresses[admission.Peer]);
                continue;
            }
            if (
                session.PeerStats.Any(stats =>
                    stats.PeerId == admission.Peer && stats.State == GGCS.SessionState.Running
                )
            )
                admission.Ready.Add(0);
            if (
                admission.Required.Where(peerAddresses.ContainsKey).Append(admission.Peer).All(admission.Ready.Contains)
            )
            {
                preparedPeers.Add(admission.Peer);
                admissions.Remove(admission.Peer);
                rosterChanged = true;
            }
        }
        int[] wanted = Roster
            .Slots.Where(slot => slot.Player is { Cpu: false })
            .Select(slot => slot.Player!.Peer)
            .Distinct()
            .ToArray();
        foreach (
            int peer in wanted.Where(peer =>
                simulationReadyPeers.Contains(peer) && !preparedPeers.Contains(peer) && !admissions.ContainsKey(peer)
            )
        )
            BeginAdmission(peer);
        if (rosterChanged && wanted.All(preparedPeers.Contains))
        {
            RefreshSelections();
            var desired = new LobbyMembership(Roster.Slots.Select(slot => slot.Player), Roster.Spectators);
            session.ProposedMembership = LobbyRosterCommand.From(session.ProposedMembership.Revision + 1, desired);
            rosterChanged = false;
        }
        if (
            !session.TryGetConfirmedCheckpoint(session.ConfirmedFrame + 1, out var state)
            || LobbySimulation.ReadRosterRevision(state) < session.ProposedMembership.Revision
        )
            return;
        foreach (
            int peer in Roster
                .Spectators.Select(player => player.Peer)
                .Where(peer => peer != 0 && !passivePeers.Contains(peer))
                .ToArray()
        )
        {
            SendBootstrap(peer, true, state, session.ConfirmedFrame + 1, [], Guid.NewGuid().ToString("N"));
            preparedPeers.Remove(peer);
            passivePeers.Add(peer);
        }
    }

    private void BeginAdmission(int peer)
    {
        var session = LiveSession!;
        if (!session.CanPreparePeer)
            return;
        long first = session.ConfirmedFrame + 1;
        if (!session.TryGetConfirmedCheckpoint(first, out var snapshot))
            return;
        int[] required = preparedPeers
            .Concat(admissions.Keys)
            .Where(value => value != peer && peerAddresses.ContainsKey(value))
            .Distinct()
            .ToArray();
        string id = Guid.NewGuid().ToString("N");
        if (!session.ConnectPeer(peer, first))
            return;
        admissions.Add(peer, new(peer, id, Now, required));
        session.RemoveSpectator(peer);
        passivePeers.Remove(peer);
        foreach (int other in required.Where(other => other != 0))
        {
            SendStateIfChanged(other);
            SendPeerControl(
                other,
                new Control
                {
                    Kind = ControlKind.PreparePeer,
                    Nonce = nonce,
                    Generation = Generation,
                    Peer = peer,
                    Tick = first,
                    Transaction = id,
                    Session = SessionId,
                    Peers = peerAddresses.Select(pair => new PeerAddress(pair.Key, pair.Value)).ToArray(),
                }
            );
        }
        SendBootstrap(peer, false, snapshot, first, required, id);
    }

    private void SendBootstrap(int peer, bool spectating, byte[] snapshot, long first, int[] required, string id)
    {
        SendStateIfChanged(peer);
        if (spectating)
        {
            LiveSession!.RemoveSpectator(peer);
            LiveSession.AddSpectator(peer, first);
        }
        var offer = new Control
        {
            Kind = ControlKind.Bootstrap,
            Nonce = nonce,
            Generation = Generation,
            Transaction = id,
            Session = SessionId,
            Tick = first,
            Size = snapshot.Length,
            Hash = Convert.ToHexString(SHA256.HashData(snapshot)),
            Spectating = spectating,
            Mesh = required,
            Disconnects = spectating ? null : LiveSession!.CaptureDisconnects(),
            Peers = peerAddresses.Select(pair => new PeerAddress(pair.Key, pair.Value)).ToArray(),
        };
        SendPeerControl(peer, offer);
        for (int offset = 0; offset < snapshot.Length; offset += CheckpointChunkSize)
            SendPeerControl(
                peer,
                new Control
                {
                    Kind = ControlKind.BootstrapChunk,
                    Nonce = nonce,
                    Generation = Generation,
                    Transaction = id,
                    Chunk = offset / CheckpointChunkSize,
                    Data = snapshot.AsSpan(offset, Math.Min(CheckpointChunkSize, snapshot.Length - offset)).ToArray(),
                }
            );
    }

    private void HandleLobbyChange(int peer, Control control)
    {
        if (
            control.Kind
            is not (
                ControlKind.Bootstrap
                or ControlKind.BootstrapChunk
                or ControlKind.PreparePeer
                or ControlKind.Prepared
            )
        )
        {
            HandleCheckpointControl(peer, control);
            return;
        }
        if (!Guid.TryParseExact(control.Transaction, "N", out _))
            return;
        if (IsHost)
        {
            if (
                control.Kind == ControlKind.Prepared
                && admissions.TryGetValue(control.Peer, out var admission)
                && admission.Id == control.Transaction
                && (admission.Required.Contains(peer) || peer == admission.Peer)
            )
                admission.Ready.Add(peer);
            return;
        }
        if (control.Kind == ControlKind.PreparePeer)
        {
            preparedLinks[control.Peer] = new(control);
        }
        else if (
            control.Kind == ControlKind.Bootstrap
            && control.Size is > 0 and <= MaximumCheckpointBytes
            && control.Tick >= 0
            && control.Tick < int.MaxValue
            && control.Session != 0
            && control.Hash.Length == 64
            && (control.Spectating || control.Disconnects != null)
        )
            incomingBootstrap = new(control);
        else if (
            control.Kind == ControlKind.BootstrapChunk
            && incomingBootstrap is { } pending
            && pending.Offer.Transaction == control.Transaction
        )
        {
            int count = (pending.Offer.Size + CheckpointChunkSize - 1) / CheckpointChunkSize;
            if (
                control.Chunk >= 0
                && control.Chunk < count
                && control.Data.Length
                    == Math.Min(CheckpointChunkSize, pending.Offer.Size - control.Chunk * CheckpointChunkSize)
            )
                pending.Chunks.TryAdd(control.Chunk, control.Data);
        }
    }

    private void InstallBootstrap()
    {
        if (
            simulation == null
            || incomingBootstrap is not { } pending
            || pending.Chunks.Count != (pending.Offer.Size + CheckpointChunkSize - 1) / CheckpointChunkSize
        )
            return;
        var offer = pending.Offer;
        byte[] snapshot = pending.Chunks.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).ToArray();
        if (Convert.ToHexString(SHA256.HashData(snapshot)) != offer.Hash)
            throw new InvalidDataException("Invalid admission checkpoint");
        LobbySession?.Dispose();
        simulation.Restore(snapshot);
        if (simulation.World.TickNumber != offer.Tick)
            throw new InvalidDataException("Invalid admission tick");
        SessionId = offer.Session;
        incoming.Clear();
        LobbySession = new LobbyNetworkSession(
            simulation,
            LocalPeer,
            SessionId,
            contentHash,
            CreateTransport(),
            rollback,
            offer.Spectating,
            offer.Mesh,
            offer.Disconnects
        );
        incomingBootstrap = null;
        localPreparation = offer.Spectating ? null : offer;
        phase = offer.Spectating ? LobbyPhase.Lobby : LobbyPhase.AwaitingSimulation;
    }
}
