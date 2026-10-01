using System.Security.Cryptography;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

internal sealed partial class MeshLobby
{
    private const int CheckpointChunkSize = 640;
    private const int MaximumCheckpointBytes = 128 * 1024;
    private const int LobbyInputDelay = 2;
    private int changeVersion;
    private int completedChange;
    private int[] committedPeers = [0];
    private int[] committedPlayers = [0];

    private sealed class LobbyCheckpoint(string id, int number, long created)
    {
        public string Id { get; } = id;
        public int Number { get; } = number;
        public long Created { get; } = created;
        public string Stage { get; set; } = "pause";
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
        public PeerAddress[] Peers { get; set; } = [];
        public string Hash { get; set; } = "";
        public int Size { get; set; }
        public bool OfferSent { get; set; }
        public bool CommitReceived { get; set; }
        public string Status =>
            Stage switch
            {
                "pause" => "Pausing lobby",
                "drain" => "Confirming lobby",
                "load" => "Connecting players",
                _ => "Updating lobby",
            };
    }

    private void CreateLobbySession(IReadOnlyDictionary<int, RollbackInput[]> initialInputs)
    {
        if (simulation == null)
            return;
        var owners = simulation.InputPlayers.Select(player => player.Peer).ToArray();
        if (owners.Length == 0)
            owners = [0];
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
        var config = new SessionConfig(
            PeerSlots,
            LocalPeer,
            contentHash,
            simulation.World,
            SessionId,
            inputDelay: LobbyInputDelay,
            activePeers: PeerIds.ToArray(),
            initialInputs: initialInputs
        );
        LobbySession = new NetworkSession(simulation, config, CreateTransport());
        committedPeers = PeerIds.ToArray();
        committedPlayers = owners.Distinct().Append(0).Distinct().Order().ToArray();
    }

    private void UpdateCheckpoint()
    {
        if (simulation == null || Starting)
            return;
        LobbySession?.Poll();
        if (LobbySession?.Error != null)
        {
            Error = LobbySession.Error;
            return;
        }
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
        if (pending.Stage == "pause" && LobbySession != null)
        {
            if (!pending.Stopped.ContainsKey(LocalPeer))
            {
                LobbySession.StopAtTick(simulation.World.TickNumber);
                pending.Stopped[LocalPeer] = Math.Max(simulation.World.TickNumber, LobbySession.LastSubmittedTick + 1);
            }
        }
        if (IsHost && pending.Stage == "pause" && pending.PausePeers.All(pending.Stopped.ContainsKey))
        {
            pending.Boundary = pending.Stopped.Values.DefaultIfEmpty(simulation.World.TickNumber).Max();
            pending.Stage = "drain";
            LobbySession?.StopAtTick(pending.Boundary);
            lastSend = -1000;
        }
        if (
            pending.Stage == "drain"
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
        if (IsHost && pending.Stage == "drain" && pending.PausePeers.All(pending.Confirmed.ContainsKey))
        {
            string hash = pending.Confirmed[0].Hash;
            if (pending.Confirmed.Values.Any(report => report.Hash != hash))
            {
                Error = "LOBBY CHECKPOINT MISMATCH";
                return;
            }
            PrepareCheckpoint(pending);
        }
        if (pending.Stage != "load")
            return;
        if (!IsHost && pending.Snapshot == null)
            AssembleCheckpoint(pending);
        if (IsHost)
        {
            if (!pending.OfferSent && MeshReady(pending.RequiredPeers))
            {
                pending.OfferSent = true;
                SendCheckpoint(pending);
            }
            if (pending.OfferSent && pending.RequiredPeers.All(pending.Loaded.Contains))
            {
                var commit = CheckpointControl("commit", pending);
                foreach (int peer in PeerIds.Where(peer => peer != 0))
                    SendPeerControl(peer, commit);
                InstallCheckpoint(pending);
            }
        }
        else if (pending.Snapshot != null && pending.CommitReceived)
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
                if (handle >= simulation.InputPlayers.Count)
                    continue;
                var player = simulation.InputPlayers[handle];
                byte[] data = new byte[delayed[index].Length * codec.Size];
                for (int offset = 0; offset < delayed[index].Length; offset++)
                    codec.Encode(delayed[index][offset], data.AsSpan(offset * codec.Size, codec.Size));
                result.Add(new(player.Peer, player.Id, data));
            }
            inputs = result.ToArray();
        }
        var report = CheckpointControl("confirmed", pending);
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
        if (pending.Match && simulation.Roster.Slots.Any(slot => slot.Player is { Spawned: false }))
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
        pending.Stage = "load";
        pending.Loaded.Add(0);
        lastSend = -1000;
    }

    private Control CheckpointControl(string kind, LobbyCheckpoint pending) =>
        new()
        {
            Kind = kind,
            Nonce = nonce,
            Generation = Generation,
            Transaction = pending.Id,
            Change = pending.Number,
            Stage = pending.Stage,
            Tick = pending.Boundary,
            Session = pending.Session,
            Starting = pending.Match,
        };

    private void SendCheckpoint(LobbyCheckpoint pending)
    {
        foreach (int peer in PeerIds.Where(peer => peer != 0))
        {
            var offer = CheckpointControl("checkpoint", pending);
            offer.Size = pending.Size;
            offer.Hash = pending.Hash;
            offer.Settings = pending.Settings;
            offer.Inputs = pending.Inputs;
            offer.Peers = pending.Peers;
            SendPeerControl(peer, offer);
            for (int offset = 0; offset < pending.Size; offset += CheckpointChunkSize)
            {
                var chunk = CheckpointControl("chunk", pending);
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
            if (pending.Stage is "pause" or "drain")
                foreach (int peer in PeerIds.Where(peer => peer != 0))
                {
                    var progress = CheckpointControl(pending.Stage, pending);
                    progress.Mesh = pending.PausePeers;
                    SendPeerControl(peer, progress);
                }
        }
        else if (pending.Stage == "pause" && pending.Stopped.TryGetValue(LocalPeer, out long tick))
        {
            var report = CheckpointControl("paused", pending);
            report.Tick = tick;
            SendPeerControl(0, report);
        }
        else if (pending.Stage == "drain" && pending.Confirmed.TryGetValue(LocalPeer, out var report))
            SendPeerControl(0, report);
        else if (pending.Stage == "load" && pending.Snapshot != null)
        {
            var loaded = CheckpointControl("loaded", pending);
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
                control.Kind == "paused"
                && pending.Stage == "pause"
                && pending.PausePeers.Contains(peer)
                && control.Tick >= 0
            )
                pending.Stopped.TryAdd(peer, control.Tick);
            if (
                control.Kind == "confirmed"
                && pending.Stage == "drain"
                && pending.PausePeers.Contains(peer)
                && control.Tick == pending.Boundary
                && ValidPendingInputs(peer, control.Inputs)
            )
                pending.Confirmed.TryAdd(peer, control);
            if (control.Kind == "loaded" && pending.Stage == "load" && control.Hash == pending.Hash)
                pending.Loaded.Add(peer);
            return;
        }
        if (control.Kind == "cancel")
        {
            if (checkpoint?.Id == control.Transaction)
            {
                LobbySession?.StopAtTick(null);
                checkpoint = null;
                completedChange = control.Change;
            }
            return;
        }
        if (control.Change < changeVersion)
            return;
        if (checkpoint == null || control.Change > changeVersion)
        {
            if (control.Kind is not ("pause" or "drain" or "checkpoint"))
                return;
            checkpoint = new(control.Transaction, control.Change, Now);
            changeVersion = control.Change;
            LobbySession?.StopAtTick(simulation?.World.TickNumber ?? 0);
        }
        var change = checkpoint;
        if (change.Id != control.Transaction)
            return;
        change.Session = control.Session;
        change.Match = control.Starting;
        if (control.Kind == "pause" && change.Stage == "pause")
        {
            change.PausePeers = control.Mesh;
            if (LobbySession != null)
                change.Stopped.TryAdd(
                    LocalPeer,
                    Math.Max(simulation!.World.TickNumber, LobbySession.LastSubmittedTick + 1)
                );
        }
        else if (control.Kind == "drain" && change.Stage is "pause" or "drain")
        {
            if (control.Tick < 0 || LobbySession != null && control.Tick < simulation!.World.TickNumber)
                throw new InvalidDataException("Invalid lobby pause boundary");
            change.Stage = "drain";
            change.Boundary = control.Tick;
            change.PausePeers = control.Mesh;
            LobbySession?.StopAtTick(control.Tick);
        }
        else if (control.Kind == "checkpoint")
        {
            if (control.Size is < 1 or > MaximumCheckpointBytes || control.Session == 0 || control.Hash.Length != 64)
                return;
            change.Stage = "load";
            change.Boundary = control.Tick;
            change.Size = control.Size;
            change.Hash = control.Hash;
            change.Inputs = control.Inputs;
            change.Peers = control.Peers;
            change.Settings = control.Settings;
        }
        else if (control.Kind == "chunk" && change.Stage == "load")
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
        else if (control.Kind == "commit")
            change.CommitReceived = true;
    }

    private bool ValidPendingInputs(int peer, PendingInput[] inputs)
    {
        var expected = simulation!
            .InputPlayers.Where(player => player.Peer == peer)
            .Select(player => player.Id)
            .Order();
        if (
            inputs.Any(input => input.Peer != peer) || !inputs.Select(input => input.Id).Order().SequenceEqual(expected)
        )
            return false;
        var codec = new RollbackInputCodec();
        foreach (var input in inputs)
        {
            if (input.Data.Length != LobbyInputDelay * codec.Size)
                return false;
            for (int offset = 0; offset < LobbyInputDelay; offset++)
                _ = codec.Decode(input.Data.AsSpan(offset * codec.Size, codec.Size));
        }
        return true;
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
        Roster.Replace(simulation.Roster.Slots, simulation.Roster.Spectators);
        if (pending.Match)
            MatchSettingsJson = pending.Settings;
        var seeds = new Dictionary<int, RollbackInput[]>();
        var codec = new RollbackInputCodec();
        for (int handle = 0; handle < simulation.InputPlayers.Count; handle++)
        {
            var player = simulation.InputPlayers[handle];
            var seed = pending.Inputs.FirstOrDefault(input => input.Peer == player.Peer && input.Id == player.Id);
            if (seed == null)
                continue;
            if (seed.Data.Length != LobbyInputDelay * codec.Size)
                throw new InvalidDataException("Invalid delayed-input checkpoint");
            seeds[handle] = Enumerable
                .Range(0, LobbyInputDelay)
                .Select(offset => codec.Decode(seed.Data.AsSpan(offset * codec.Size, codec.Size)))
                .ToArray();
        }
        completedChange = pending.Number;
        checkpoint = null;
        initializedSimulation = true;
        matchRequested = false;
        if (pending.Match)
        {
            FreezeRoster();
            Starting = Ready = true;
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
        var cancel = CheckpointControl("cancel", pending);
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
            Roster.Replace(simulation.Roster.Slots, simulation.Roster.Spectators);
            foreach (int peer in committedPeers.Where(peer => !peerAddresses.ContainsKey(peer)))
                Roster.RemovePeer(peer);
        }
        completedChange = pending.Number;
        checkpoint = null;
        matchRequested = false;
        rosterChanged = !committedPeers.SequenceEqual(PeerIds);
        Notice = notice;
        revision++;
        lastSend = -1000;
    }
}
