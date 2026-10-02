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
        public bool Match { get; set; }
        public string Settings { get; set; } = "";
        public int[] PausePeers { get; set; } = [];
        public int[] RequiredPeers { get; set; } = [];
        public Dictionary<int, long> Stopped { get; } = new();
        public Dictionary<int, Control> Confirmed { get; } = new();
        public HashSet<int> Loaded { get; } = new();
        public Dictionary<int, byte[]> Chunks { get; } = new();
        public byte[]? Snapshot { get; set; }
        public byte[]? Previous { get; set; }
        public PendingInput[] Inputs { get; set; } = [];
        public Dictionary<(int Peer, int Id), PendingInput> Prefixes { get; } = new();
        public int ExpectedInputs { get; set; }
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

    private void CreateLobbySession(IReadOnlyDictionary<int, RollbackInput[]> initialInputs)
    {
        if (simulation == null)
            return;
        var owners = simulation.InputSources.Select(player => player.Peer).ToArray();
        PeerSlots = Enumerable
            .Range(0, peerAddresses.Keys.Max() + 1)
            .Select(peer =>
                owners
                    .Select((owner, index) => (owner, index))
                    .Where(item => item.owner == peer)
                    .Select(item => item.index)
                    .ToArray()
            )
            .ToArray();
        InputPlayerSlots = simulation.InputRooms.ToArray();
        var config = new SessionConfig(
            PeerSlots,
            LocalPeer,
            contentHash,
            simulation.World,
            SessionId,
            rollback: rollback,
            activePeers: PeerIds.ToArray(),
            initialInputs: initialInputs,
            inputPlayerSlots: simulation.InputRooms.ToArray()
        );
        LobbySession = new NetworkSession(simulation, config, CreateTransport());
        committedPeers = PeerIds.ToArray();
        committedPlayers = owners.Distinct().Append(0).Distinct().Order().ToArray();
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
        if (IsHost && checkpoint == null && rosterChanged)
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
        rosterChanged = false;
        var pending = new LobbyCheckpoint(Guid.NewGuid().ToString("N"), ++changeVersion, Now)
        {
            Session = NewSessionId(),
            Match = matchRequested,
            Settings = MatchSettingsJson,
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
        PendingInput[] inputs = [];
        if (LobbySession == null)
            snapshot = simulation.Capture();
        else
        {
            if (
                !LobbySession.AllPeersConfirmed(pending.Boundary - 1)
                || !LobbySession.TryGetConfirmedCheckpoint(pending.Boundary, out snapshot)
            )
                return null;
            var delayed = LobbySession.ExportPendingLocalInputs(pending.Boundary);
            var result = new List<PendingInput>();
            var codec = new RollbackInputCodec();
            for (int index = 0; index < delayed.Length; index++)
            {
                int handle = LobbySession.LocalSlots[index];
                if (handle >= simulation.InputSources.Count)
                    continue;
                var player = simulation.InputSources[handle];
                byte[] data = new byte[delayed[index].Length * codec.Size];
                for (int offset = 0; offset < delayed[index].Length; offset++)
                    codec.Encode(delayed[index][offset], data.AsSpan(offset * codec.Size, codec.Size));
                result.Add(new(player.Peer, player.Id, data));
            }
            inputs = result.ToArray();
        }
        var report = CheckpointControl(ControlKind.Confirmed, pending);
        report.Tick = pending.Boundary;
        report.Hash = Convert.ToHexString(SHA256.HashData(snapshot));
        report.Inputs = inputs;
        return report;
    }

    private void PrepareCheckpoint(LobbyCheckpoint pending)
    {
        pending.Previous = simulation!.Capture();
        pending.Inputs = pending.Confirmed.Values.SelectMany(report => report.Inputs).ToArray();
        simulation.ApplyRoster(Roster);
        if (pending.Match && simulation.Membership.Rooms.Any(player => player is { Spawned: false }))
        {
            simulation.Restore(pending.Previous);
            CancelCheckpoint("SPAWN ALL PLAYERS");
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
            Starting = pending.Match,
        };

    private void SendCheckpoint(LobbyCheckpoint pending)
    {
        foreach (int peer in PeerIds.Where(peer => peer != 0))
        {
            var offer = CheckpointControl(ControlKind.Checkpoint, pending);
            offer.Size = pending.Size;
            offer.Hash = pending.Hash;
            offer.Settings = pending.Settings;
            offer.InputCount = pending.Inputs.Length;
            offer.Peers = pending.Peers;
            SendPeerControl(peer, offer);
            SendInputPrefixes(peer, pending, pending.Inputs);
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

    private void SendInputPrefixes(int peer, LobbyCheckpoint pending, PendingInput[] inputs)
    {
        foreach (var input in inputs)
        {
            var message = CheckpointControl(ControlKind.InputPrefix, pending);
            message.Inputs = [input];
            SendPeerControl(peer, message);
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
            SendInputPrefixes(0, pending, report.Inputs);
            SendPeerControl(0, report with { Inputs = [] });
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
            if (control.Kind == ControlKind.InputPrefix && pending.Stage == CheckpointPhase.Confirming)
            {
                foreach (var input in control.Inputs)
                    if (
                        input.Peer == peer
                        && simulation!.InputSources.Any(player => player.Peer == peer && player.Id == input.Id)
                    )
                        pending.Prefixes[(peer, input.Id)] = input;
                return;
            }
            if (control.Kind == ControlKind.Confirmed)
                control = control with
                {
                    Inputs = pending.Prefixes.Values.Where(input => input.Peer == peer).ToArray(),
                };
            if (
                control.Kind == ControlKind.Confirmed
                && pending.Stage == CheckpointPhase.Confirming
                && pending.PausePeers.Contains(peer)
                && control.Tick == pending.Boundary
                && ValidPendingInputs(peer, control.Inputs)
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
        change.Match = control.Starting;
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
            change.ExpectedInputs = control.InputCount;
            change.Peers = control.Peers;
            change.Settings = control.Settings;
        }
        else if (control.Kind == ControlKind.InputPrefix && change.Stage == CheckpointPhase.Loading)
        {
            foreach (var input in control.Inputs)
                if (
                    change.Prefixes.Count < SessionConfig.MaxInputs
                    || change.Prefixes.ContainsKey((input.Peer, input.Id))
                )
                    change.Prefixes[(input.Peer, input.Id)] = input;
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

    private bool ValidPendingInputs(int peer, PendingInput[] inputs)
    {
        var expected = simulation!
            .InputSources.Where(player => player.Peer == peer)
            .Select(player => player.Id)
            .Order();
        if (
            inputs.Any(input => input.Peer != peer) || !inputs.Select(input => input.Id).Order().SequenceEqual(expected)
        )
            return false;
        var codec = new RollbackInputCodec();
        foreach (var input in inputs)
        {
            if (
                input.Data.Length % codec.Size != 0
                || input.Data.Length / codec.Size > RollbackPreferences.InputCapacityFrames
            )
                return false;
            for (int offset = 0; offset < input.Data.Length / codec.Size; offset++)
                _ = codec.Decode(input.Data.AsSpan(offset * codec.Size, codec.Size));
        }
        return true;
    }

    private void AssembleCheckpoint(LobbyCheckpoint change)
    {
        int count = (change.Size + CheckpointChunkSize - 1) / CheckpointChunkSize;
        if (
            simulation == null
            || change.Size == 0
            || change.Chunks.Count != count
            || change.Snapshot != null
            || change.Prefixes.Count != change.ExpectedInputs
        )
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
        change.Inputs = change.Prefixes.Values.ToArray();
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
        if (pending.Match)
            MatchSettingsJson = pending.Settings;
        var seeds = new Dictionary<int, RollbackInput[]>();
        var codec = new RollbackInputCodec();
        for (int handle = 0; handle < simulation.InputSources.Count; handle++)
        {
            var player = simulation.InputSources[handle];
            var seed = pending.Inputs.FirstOrDefault(input => input.Peer == player.Peer && input.Id == player.Id);
            if (seed == null)
                continue;
            if (
                seed.Data.Length % codec.Size != 0
                || seed.Data.Length / codec.Size > RollbackPreferences.InputCapacityFrames
            )
                throw new InvalidDataException("Invalid delayed-input checkpoint");
            seeds[handle] = Enumerable
                .Range(0, seed.Data.Length / codec.Size)
                .Select(offset => codec.Decode(seed.Data.AsSpan(offset * codec.Size, codec.Size)))
                .ToArray();
        }
        completedChange = pending.Number;
        checkpoint = null;
        phase = LobbyPhase.Lobby;
        matchRequested = false;
        if (pending.Match)
        {
            FreezeRoster();
            phase = LobbyPhase.Match;
        }
        else
            CreateLobbySession(seeds);
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
