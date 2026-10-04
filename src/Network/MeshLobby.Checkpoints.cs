using System.Security.Cryptography;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

internal sealed partial class MeshLobby
{
    private const int CheckpointChunkSize = 640;
    private const int MaximumCheckpointBytes = 128 * 1024;
    private int changeVersion;
    private int completedChange;
    private int[] committedPeers = [0];
    private int[] committedPlayers = [0];

    private sealed class LobbyCheckpoint(string id, int number, long created)
    {
        public string Id { get; } = id;
        public int Number { get; } = number;
        public long Created { get; } = created;
        public CheckpointPhase Stage { get; private set; } = CheckpointPhase.Pausing;
        public long Boundary { get; set; } = -1;
        public ulong Session { get; set; }
        public string Settings { get; set; } = "";
        public LobbyPlayer?[] Players { get; set; } = [];
        public int[] PausePeers { get; set; } = [];
        public int[] RequiredPeers { get; set; } = [];
        public Dictionary<int, long> Stopped { get; } = new();
        public Dictionary<int, Control> Confirmed { get; } = new();
        public HashSet<int> Loaded { get; } = new();
        public Dictionary<int, byte[]> Chunks { get; } = new();
        public byte[]? Snapshot { get; set; }
        public byte[]? Previous { get; set; }
        public PeerAddress[] Peers { get; set; } = [];
        public string Hash { get; set; } = "";
        public int Size { get; set; }

        public void MoveTo(CheckpointPhase next)
        {
            if (next == Stage)
                return;
            bool valid = (Stage, next) switch
            {
                (CheckpointPhase.Pausing, CheckpointPhase.Confirming or CheckpointPhase.Loading) => true,
                (CheckpointPhase.Confirming, CheckpointPhase.Connecting or CheckpointPhase.Loading) => true,
                (CheckpointPhase.Connecting, CheckpointPhase.Loading) => true,
                (CheckpointPhase.Loading, CheckpointPhase.AwaitingCommit or CheckpointPhase.Committed) => true,
                (CheckpointPhase.AwaitingCommit, CheckpointPhase.Committed) => true,
                _ => false,
            };
            if (!valid)
                throw new InvalidDataException($"Invalid lobby transition: {Stage} to {next}");
            Stage = next;
        }

        public string Status =>
            Stage switch
            {
                CheckpointPhase.Pausing => "Pausing lobby",
                CheckpointPhase.Confirming => "Confirming lobby",
                CheckpointPhase.Connecting or CheckpointPhase.Loading or CheckpointPhase.AwaitingCommit =>
                    "Connecting players",
                _ => "Updating lobby",
            };
    }

    private void CreateLobbySession()
    {
        if (simulation == null)
            return;
        LobbySession = new LobbyNetworkSession(
            simulation,
            LocalPeer,
            SessionId,
            compatibility,
            CreateTransport(),
            rollback,
            false,
            []
        );
        committedPeers = PeerIds.ToArray();
        committedPlayers = [0];
    }

    private void UpdateCheckpoint()
    {
        if (simulation == null || Starting)
            return;
        if (LobbySession?.Error != null)
        {
            Error = LobbySession.Error;
            return;
        }
        UpdateCpuEdits();
        if (IsHost && checkpoint == null && matchRequested)
            BeginCheckpoint();
        var pending = checkpoint;
        if (pending == null)
            return;
        if (Now - pending.Created > 15000)
        {
            if (IsHost)
                CancelCheckpoint("LOBBY CHANGE TIMED OUT");
            else
                Error = "LOBBY CHANGE TIMED OUT";
            return;
        }
        if (pending.Stage == CheckpointPhase.Pausing && LobbySession != null)
        {
            if (!pending.Stopped.ContainsKey(LocalPeer))
            {
                LobbySession.StopAtTick(simulation.World.TickNumber);
                pending.Stopped[LocalPeer] = Math.Max(simulation.World.TickNumber, LobbySession.LastSubmittedTick + 1);
            }
        }
        if (IsHost && pending.Stage == CheckpointPhase.Pausing && pending.PausePeers.All(pending.Stopped.ContainsKey))
        {
            pending.Boundary = pending.Stopped.Values.DefaultIfEmpty(simulation.World.TickNumber).Max();
            pending.MoveTo(CheckpointPhase.Confirming);
            LobbySession?.StopAtTick(pending.Boundary);
            lastSend = -1000;
        }
        if (
            pending.Stage == CheckpointPhase.Confirming
            && !pending.Confirmed.ContainsKey(LocalPeer)
            && pending.PausePeers.Contains(LocalPeer)
        )
        {
            var ready = CapturePause(pending);
            if (ready != null)
            {
                pending.Confirmed[LocalPeer] = ready;
                lastSend = -1000;
            }
        }
        if (
            IsHost
            && pending.Stage == CheckpointPhase.Confirming
            && pending.PausePeers.All(pending.Confirmed.ContainsKey)
        )
        {
            string hash = pending.Confirmed[0].Hash;
            if (pending.Confirmed.Values.Any(report => report.Hash != hash))
            {
                Error = "LOBBY CHECKPOINT MISMATCH";
                return;
            }
            PrepareCheckpoint(pending);
        }
        if (pending.Stage is CheckpointPhase.Pausing or CheckpointPhase.Confirming)
            return;
        if (!IsHost && pending.Snapshot == null)
            AssembleCheckpoint(pending);
        if (IsHost)
        {
            if (pending.Stage == CheckpointPhase.Connecting && MeshReady(pending.RequiredPeers))
            {
                pending.MoveTo(CheckpointPhase.Loading);
                SendCheckpoint(pending);
            }
            if (pending.Stage == CheckpointPhase.Loading && pending.RequiredPeers.All(pending.Loaded.Contains))
            {
                var commit = CheckpointControl(ControlKind.Commit, pending);
                foreach (int peer in PeerIds.Where(peer => peer != 0))
                    SendPeerControl(peer, commit);
                InstallCheckpoint(pending);
            }
        }
        else if (pending.Snapshot != null && pending.Stage == CheckpointPhase.Committed)
            InstallCheckpoint(pending);
    }

    private void BeginCheckpoint()
    {
        RefreshSelections();
        committedPeers = PeerIds.ToArray();
        committedPlayers = simulation!
            .InputSources.Select(source => source.Peer)
            .Distinct()
            .Append(0)
            .Distinct()
            .ToArray();
        rosterChanged = false;
        var pending = new LobbyCheckpoint(Guid.NewGuid().ToString("N"), ++changeVersion, Now)
        {
            Session = NewSessionId(),
            Settings = MatchSettingsJson,
            Players = Roster.Slots.Select(slot => slot.Player).ToArray(),
            PausePeers = committedPlayers.Where(peerAddresses.ContainsKey).ToArray(),
            RequiredPeers = Roster
                .Slots.Where(slot => slot.Player != null)
                .Select(slot => slot.Player!.Peer)
                .Append(0)
                .Distinct()
                .Order()
                .ToArray(),
        };
        checkpoint = pending;
        phase = LobbyPhase.Updating;
        if (LobbySession == null)
        {
            pending.PausePeers = [0];
            pending.Stopped[0] = simulation!.World.TickNumber;
        }
        else
        {
            LobbySession.StopAtTick(simulation!.World.TickNumber);
            pending.Stopped[0] = Math.Max(simulation.World.TickNumber, LobbySession.LastSubmittedTick + 1);
        }
        lastSend = -1000;
    }

    private Control? CapturePause(LobbyCheckpoint pending)
    {
        if (simulation!.World.TickNumber != pending.Boundary)
            return null;
        byte[] snapshot;
        if (LobbySession == null)
            snapshot = simulation.Capture();
        else
        {
            if (
                !LobbySession.AllPeersConfirmed(pending.Boundary - 1)
                || !LobbySession.TryGetConfirmedCheckpoint(pending.Boundary, out snapshot)
            )
                return null;
        }
        var report = CheckpointControl(ControlKind.Confirmed, pending);
        report.Tick = pending.Boundary;
        report.Hash = Convert.ToHexString(SHA256.HashData(snapshot));
        return report;
    }

    private void PrepareCheckpoint(LobbyCheckpoint pending)
    {
        pending.Previous = simulation!.Capture();
        simulation.ApplyRoster(Roster);
        if (simulation.Membership.Rooms.Any(player => player is { Spawned: false }))
        {
            simulation.Restore(pending.Previous);
            CancelCheckpoint("SPAWN ALL PLAYERS");
            return;
        }
        if (!pending.Players.SequenceEqual(simulation.Membership.Rooms))
        {
            simulation.Restore(pending.Previous);
            CancelCheckpoint("PLAYERS CHANGED; START AGAIN");
            return;
        }
        pending.Snapshot = simulation.Capture();
        simulation.Restore(pending.Previous);
        if (pending.Snapshot.Length > MaximumCheckpointBytes)
            throw new InvalidOperationException("Lobby checkpoint exceeded its size limit");
        pending.Size = pending.Snapshot.Length;
        pending.Peers = peerAddresses.Select(pair => new PeerAddress(pair.Key, pair.Value)).ToArray();
        pending.Hash = Convert.ToHexString(SHA256.HashData(pending.Snapshot));
        pending.MoveTo(CheckpointPhase.Connecting);
        pending.Loaded.Add(0);
        lastSend = -1000;
    }

    private Control CheckpointControl(ControlKind kind, LobbyCheckpoint pending) =>
        new()
        {
            Kind = kind,
            Nonce = nonce,
            Generation = Generation,
            Transaction = pending.Id,
            Change = pending.Number,
            Tick = pending.Boundary,
            Session = pending.Session,
            Starting = true,
        };

    private void SendCheckpoint(LobbyCheckpoint pending)
    {
        foreach (int peer in PeerIds.Where(peer => peer != 0))
        {
            var offer = CheckpointControl(ControlKind.Checkpoint, pending);
            offer.Size = pending.Size;
            offer.Hash = pending.Hash;
            offer.Settings = pending.Settings;
            offer.Peers = pending.Peers;
            SendPeerControl(peer, offer);
            for (int offset = 0; offset < pending.Size; offset += CheckpointChunkSize)
            {
                var chunk = CheckpointControl(ControlKind.Chunk, pending);
                chunk.Chunk = offset / CheckpointChunkSize;
                chunk.Data = pending
                    .Snapshot!.AsSpan(offset, Math.Min(CheckpointChunkSize, pending.Size - offset))
                    .ToArray();
                SendPeerControl(peer, chunk);
            }
        }
    }

    private void SendCheckpointProgress()
    {
        var pending = checkpoint;
        if (pending == null)
            return;
        if (IsHost)
        {
            if (pending.Stage is CheckpointPhase.Pausing or CheckpointPhase.Confirming)
                foreach (int peer in PeerIds.Where(peer => peer != 0))
                {
                    var progress = CheckpointControl(
                        pending.Stage == CheckpointPhase.Pausing ? ControlKind.Pause : ControlKind.Confirm,
                        pending
                    );
                    progress.Mesh = pending.PausePeers;
                    SendPeerControl(peer, progress);
                }
        }
        else if (pending.Stage == CheckpointPhase.Pausing && pending.Stopped.TryGetValue(LocalPeer, out long tick))
        {
            var report = CheckpointControl(ControlKind.Paused, pending);
            report.Tick = tick;
            SendPeerControl(0, report);
        }
        else if (
            pending.Stage == CheckpointPhase.Confirming
            && pending.Confirmed.TryGetValue(LocalPeer, out var report)
        )
        {
            SendPeerControl(0, report);
        }
        else if (pending.Stage == CheckpointPhase.AwaitingCommit && pending.Snapshot != null)
        {
            var loaded = CheckpointControl(ControlKind.Loaded, pending);
            loaded.Hash = pending.Hash;
            SendPeerControl(0, loaded);
        }
    }

    private void HandleCheckpointControl(int peer, Control control)
    {
        if (!Guid.TryParseExact(control.Transaction, "N", out _) || control.Change <= completedChange)
            return;
        if (IsHost)
        {
            var pending = checkpoint;
            if (pending == null || control.Transaction != pending.Id || control.Change != pending.Number)
                return;
            if (
                control.Kind == ControlKind.Paused
                && pending.Stage == CheckpointPhase.Pausing
                && pending.PausePeers.Contains(peer)
                && control.Tick >= 0
            )
                pending.Stopped.TryAdd(peer, control.Tick);
            if (
                control.Kind == ControlKind.Confirmed
                && pending.Stage == CheckpointPhase.Confirming
                && pending.PausePeers.Contains(peer)
                && control.Tick == pending.Boundary
            )
                pending.Confirmed.TryAdd(peer, control);
            if (
                control.Kind == ControlKind.Loaded
                && pending.Stage == CheckpointPhase.Loading
                && control.Hash == pending.Hash
            )
                pending.Loaded.Add(peer);
            return;
        }
        if (control.Kind == ControlKind.Cancel)
        {
            if (checkpoint?.Id == control.Transaction)
            {
                LobbySession?.StopAtTick(null);
                checkpoint = null;
                phase = LobbySession == null ? LobbyPhase.AwaitingSimulation : LobbyPhase.Lobby;
                completedChange = control.Change;
            }
            return;
        }
        if (control.Change < changeVersion)
            return;
        if (checkpoint == null || control.Change > changeVersion)
        {
            if (control.Kind is not (ControlKind.Pause or ControlKind.Confirm or ControlKind.Checkpoint))
                return;
            checkpoint = new(control.Transaction, control.Change, Now);
            phase = LobbyPhase.Updating;
            changeVersion = control.Change;
            LobbySession?.StopAtTick(simulation?.World.TickNumber ?? 0);
        }
        var change = checkpoint;
        if (change.Id != control.Transaction)
            return;
        change.Session = control.Session;
        if (control.Kind == ControlKind.Pause && change.Stage == CheckpointPhase.Pausing)
        {
            change.PausePeers = control.Mesh;
            if (
                LobbySession != null
                && change.Stopped.TryAdd(
                    LocalPeer,
                    Math.Max(simulation!.World.TickNumber, LobbySession.LastSubmittedTick + 1)
                )
            )
                lastSend = -1000;
        }
        else if (
            control.Kind == ControlKind.Confirm
            && change.Stage is CheckpointPhase.Pausing or CheckpointPhase.Confirming
        )
        {
            if (control.Tick < 0 || LobbySession != null && control.Tick < simulation!.World.TickNumber)
                throw new InvalidDataException("Invalid lobby pause boundary");
            change.MoveTo(CheckpointPhase.Confirming);
            change.Boundary = control.Tick;
            change.PausePeers = control.Mesh;
            LobbySession?.StopAtTick(control.Tick);
        }
        else if (control.Kind == ControlKind.Checkpoint)
        {
            if (control.Size is < 1 or > MaximumCheckpointBytes || control.Session == 0 || control.Hash.Length != 64)
                return;
            if (change.Stage is CheckpointPhase.AwaitingCommit or CheckpointPhase.Committed)
                return;
            change.MoveTo(CheckpointPhase.Loading);
            change.Boundary = control.Tick;
            change.Size = control.Size;
            change.Hash = control.Hash;
            change.Peers = control.Peers;
            change.Settings = control.Settings;
        }
        else if (
            control.Kind == ControlKind.Chunk
            && change.Stage is CheckpointPhase.Loading or CheckpointPhase.Committed
        )
        {
            int count = (change.Size + CheckpointChunkSize - 1) / CheckpointChunkSize;
            if (
                control.Chunk < 0
                || control.Chunk >= count
                || control.Data.Length
                    != Math.Min(CheckpointChunkSize, change.Size - control.Chunk * CheckpointChunkSize)
            )
                return;
            change.Chunks.TryAdd(control.Chunk, control.Data);
            AssembleCheckpoint(change);
        }
        else if (control.Kind == ControlKind.Commit)
        {
            if (change.Stage is CheckpointPhase.Loading or CheckpointPhase.AwaitingCommit)
                change.MoveTo(CheckpointPhase.Committed);
        }
    }

    private void AssembleCheckpoint(LobbyCheckpoint change)
    {
        int count = (change.Size + CheckpointChunkSize - 1) / CheckpointChunkSize;
        if (simulation == null || change.Size == 0 || change.Chunks.Count != count || change.Snapshot != null)
            return;
        byte[] snapshot = change.Chunks.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).ToArray();
        if (Convert.ToHexString(SHA256.HashData(snapshot)) != change.Hash)
            throw new InvalidDataException("Invalid lobby checkpoint hash");
        byte[] previous = simulation.Capture();
        try
        {
            simulation.Restore(snapshot);
            if (simulation.World.TickNumber != change.Boundary)
                throw new InvalidDataException("Invalid checkpoint tick");
        }
        finally
        {
            simulation.Restore(previous);
        }
        change.Snapshot = snapshot;
        if (change.Stage == CheckpointPhase.Loading)
            change.MoveTo(CheckpointPhase.AwaitingCommit);
        lastSend = -1000;
    }

    private void InstallCheckpoint(LobbyCheckpoint pending)
    {
        if (simulation == null || pending.Snapshot == null)
            return;
        SessionId = pending.Session;
        LobbySession?.Dispose();
        LobbySession = null;
        incoming.Clear();
        if (!IsHost)
            InstallAddresses(pending.Peers);
        simulation.Restore(pending.Snapshot);
        Roster.ApplyMembership(simulation.Membership);
        MatchSettingsJson = pending.Settings;
        completedChange = pending.Number;
        checkpoint = null;
        phase = LobbyPhase.Lobby;
        matchRequested = false;
        FreezeRoster();
        phase = LobbyPhase.Match;
        revision++;
        lastSend = -1000;
    }

    private void CancelCheckpoint(string notice)
    {
        var pending = checkpoint;
        if (pending == null)
            return;
        var cancel = CheckpointControl(ControlKind.Cancel, pending);
        foreach (int peer in PeerIds.Where(peer => peer != 0))
            SendPeerControl(peer, cancel);
        var newcomers = PeerIds.Where(peer => !committedPeers.Contains(peer)).ToArray();
        foreach (int peer in newcomers)
        {
            string address = peerAddresses[peer];
            Reject(address, notice, clientNonces[peer]);
            addresses.Remove(address);
            peerAddresses.Remove(peer);
            clientNonces.Remove(peer);
            access.Remove(peer);
            versions.Remove(peer);
            epochs.Remove(peer);
            lastSeen.Remove(peer);
            sentRevisions.Remove(peer);
        }
        LobbySession?.StopAtTick(null);
        if (simulation != null)
        {
            Roster.ApplyMembership(simulation.Membership);
            foreach (int peer in committedPeers.Where(peer => !peerAddresses.ContainsKey(peer)))
                Roster.RemovePeer(peer);
        }
        completedChange = pending.Number;
        checkpoint = null;
        matchRequested = false;
        rosterChanged = !committedPeers.SequenceEqual(PeerIds);
        phase = LobbySession == null ? LobbyPhase.AwaitingSimulation : LobbyPhase.Lobby;
        Notice = notice;
        revision++;
        lastSend = -1000;
    }
}
