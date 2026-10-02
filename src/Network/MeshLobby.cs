using System.Text.Json;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

internal sealed partial class MeshLobby : IGameLobby, IPeerTransport
{
    private const uint Magic = 0x46534D31;
    private readonly IWire wire;
    private readonly long createdAt;
    private readonly string? host;
    private readonly string contentHash;
    private readonly bool invited;
    private readonly Func<string, bool> isFriend;
    private readonly Dictionary<int, LobbyAccess> access = new();
    private readonly Dictionary<string, int> addresses = new();
    private readonly Dictionary<int, string> peerAddresses = new();
    private readonly Dictionary<int, string> clientNonces = new();
    private readonly Dictionary<int, int> versions = new();
    private readonly Dictionary<int, int> epochs = new();
    private readonly Dictionary<int, long> lastSeen = new();
    private readonly Dictionary<int, long> meshSeen = new();
    private readonly Dictionary<int, int[]> meshReports = new();
    private readonly Dictionary<int, int> sentRevisions = new();
    private readonly Dictionary<string, (string Nonce, string Reason)> rejectedClients = new();
    private readonly HashSet<string> banned = new();
    private readonly Queue<Datagram> incoming = new();
    private readonly string clientNonce = Guid.NewGuid().ToString("N");
    private string nonce = Guid.NewGuid().ToString("N");
    private RollbackPreferences rollback = new();
    private readonly Dictionary<int, RollbackPreferences> peerRollback = new();
    public RollbackPreferences RollbackSettings => rollback;
    public IReadOnlyDictionary<int, RollbackPreferences> PeerRollbackSettings => peerRollback;

    public void SetRollbackSettings(RollbackPreferences preferences)
    {
        if (!preferences.IsValid)
            throw new ArgumentException("Invalid rollback preferences");
        if (preferences == rollback)
            return;
        rollback = preferences;
        if (Connected)
            peerRollback[LocalPeer] = preferences;
        LobbySession?.SetTiming(preferences);
        revision++;
        lastSend = -1000;
    }

    private LobbyAccess localAccess;
    private LobbyPlayer[] requested;
    private bool requestedSpectating;
    private int requestVersion;
    private int acknowledgedRequest;
    private int requestEpoch;
    private int revision;
    private int receivedRevision = -1;
    private long lastSend = -1000;
    private bool disposed;
    private LobbySimulation? simulation;
    private LobbyCheckpoint? checkpoint;
    private bool rosterChanged;
    private bool matchRequested;
    private LobbyPhase phase = LobbyPhase.AwaitingSimulation;

    public bool IsHost => host == null;
    public bool Connected => LocalPeer >= 0;
    public bool Starting => phase == LobbyPhase.Match;
    public bool Ready => Starting;
    public int Generation { get; private set; }
    public ulong SessionId { get; private set; }
    public IReadOnlyList<int> PeerIds => peerAddresses.Keys.Order().ToArray();
    public NetworkSession? LobbySession { get; private set; }
    public bool SimulationReady => phase == LobbyPhase.Lobby;
    public bool Transitioning => phase is LobbyPhase.AwaitingSimulation or LobbyPhase.Updating;
    public string Status =>
        checkpoint?.Status
        ?? (
            Starting ? "Starting match"
            : Connected ? "Connected"
            : "Connecting"
        );
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public int LocalPeer { get; private set; } = -1;
    public LobbyRoster Roster { get; } = new();
    public IReadOnlyList<LobbyPlayer> PendingLocalPlayers => IsHost ? Roster.Players(0) : requested;
    public bool PendingLocalSpectating => IsHost ? Roster.Spectator(0) != null : requestedSpectating;
    public bool LocalRequestPending => !IsHost && requestVersion > acknowledgedRequest;
    public LobbyAccess Access => IsHost ? default : localAccess;
    public int[][] PeerSlots { get; private set; } = [];
    public int[] InputPlayerSlots { get; private set; } = [];
    public int[] PlayerTeams { get; private set; } = [];
    public int[] PlayerColors { get; private set; } = [];
    public string MatchSettingsJson { get; private set; }
    private long Now => wire.TimeMilliseconds;
    public long TimeMilliseconds => Now;

    public MeshLobby(
        IWire wire,
        string? host,
        int capacity,
        LobbyPlayer[] players,
        string hash,
        string settings,
        IReadOnlyList<LobbySlot>? initialRooms = null,
        bool invited = false,
        Func<string, bool>? isFriend = null
    )
    {
        this.wire = wire;
        createdAt = wire.TimeMilliseconds;
        this.host = host;
        this.invited = invited;
        this.isFriend = isFriend ?? (_ => false);
        contentHash = hash;
        MatchSettingsJson = settings;
        requested = players.ToArray();
        if (settings.Length > 4096 || hash.Length > 128 || !Roster.SetPlayers(0, players))
            throw new ArgumentException("Invalid lobby configuration");
        if (IsHost)
        {
            if (initialRooms != null)
            {
                Roster.Replace(initialRooms);
                if (
                    Roster.Slots.Any(slot => slot.Player is { Peer: not 0 })
                    || !Roster.Players(0).SequenceEqual(players)
                )
                    throw new ArgumentException("Initial rooms must belong to the local party");
            }
            if (!Roster.SetCapacity(capacity))
                throw new ArgumentException("Invalid lobby capacity");
            LocalPeer = 0;
            peerRollback[0] = rollback;
            peerAddresses[0] = wire.LocalAddress;
            SessionId = NewSessionId();
        }
        else
        {
            if (players.Length == 0 || players.Any(player => player.Cpu))
                throw new ArgumentException("Guests must bring at least one human player");
            Roster.SetPlayers(0, []);
            peerAddresses[0] = host!;
            wire.SetPeers([host!]);
        }
    }

    private static ulong NewSessionId()
    {
        ulong id;
        do
        {
            id = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray());
        } while (id == 0);
        return id;
    }

    public void AttachSimulation(LobbySimulation value)
    {
        if (ReferenceEquals(simulation, value) || Starting)
            return;
        LobbySession?.Dispose();
        LobbySession = null;
        simulation = value;
        phase = LobbyPhase.AwaitingSimulation;
        if (IsHost)
        {
            simulation.ApplyRoster(Roster);
            rosterChanged = addresses.Count > 0;
            if (!rosterChanged)
            {
                CreateLobbySession(new Dictionary<int, RollbackInput[]>());
                phase = LobbyPhase.Lobby;
            }
        }
    }

    private void Changed()
    {
        PublishRoster();
        rosterChanged = true;
    }

    public void Kick(int peer, bool ban)
    {
        if (!IsHost || Starting || peer <= 0)
            return;
        string? address = peerAddresses.GetValueOrDefault(peer);
        if (address == null)
            return;
        if (ban)
            banned.Add(address);
        Reject(address, ban ? "BANNED" : "REMOVED BY HOST", clientNonces[peer]);
        RemovePeer(address);
    }

    public void RemovePeer(string address)
    {
        if (!IsHost || !addresses.Remove(address, out int peer))
            return;
        bool wasPlaying =
            Roster.Players(peer).Length > 0 || simulation?.InputSources.Any(player => player.Peer == peer) == true;
        if (Starting && wasPlaying)
        {
            ReturnToLobby();
            Notice = "PLAYER LEFT";
        }
        if (wasPlaying)
            LobbySession?.DisconnectPeer(peer);
        Roster.RemovePeer(peer);
        peerAddresses.Remove(peer);
        peerRollback.Remove(peer);
        access.Remove(peer);
        rejectedClients.TryAdd(address, (clientNonces[peer], "CONNECTION CLOSED"));
        clientNonces.Remove(peer);
        epochs.Remove(peer);
        versions.Remove(peer);
        lastSeen.Remove(peer);
        meshSeen.Remove(peer);
        meshReports.Remove(peer);
        sentRevisions.Remove(peer);
        if (checkpoint != null)
            CancelCheckpoint("PLAYER LEFT");
        Changed();
    }

    public void SetMatchSettings(string settings)
    {
        if (!IsHost || Starting || settings.Length > 4096 || settings == MatchSettingsJson)
            return;
        MatchSettingsJson = settings;
        revision++;
        lastSend = -1000;
    }

    public bool StartMatch(string settings)
    {
        RefreshSelections();
        if (
            !IsHost
            || Starting
            || checkpoint != null
            || HasPendingSlotEdits
            || !SimulationReady
            || Roster.Count < 2
            || settings.Length > 4096
        )
            return false;
        if (Roster.Slots.Any(slot => slot.Player is { Spawned: false }))
        {
            Notice = "SPAWN ALL PLAYERS";
            return false;
        }
        if (Roster.Players(0).Length == 0 && Roster.Spectator(0) == null)
        {
            Notice = "JOIN OR SPECTATE FIRST";
            return false;
        }
        MatchSettingsJson = settings;
        matchRequested = true;
        Changed();
        return true;
    }

    public bool ReturnToLobby()
    {
        if (!IsHost || !Starting || Error != null)
            return false;
        Generation++;
        SessionId = NewSessionId();
        phase = LobbyPhase.AwaitingSimulation;
        simulation = null;
        LobbySession?.Dispose();
        LobbySession = null;
        checkpoint = null;
        incoming.Clear();
        foreach (int peer in addresses.Values)
            epochs[peer]++;
        Changed();
        return true;
    }

    public void Poll()
    {
        if (disposed || Error != null)
            return;
        wire.Poll();
        if (wire.Error != null)
        {
            Error = wire.Error;
            return;
        }
        if (!Connected && Now - createdAt > 30000)
        {
            Error = "CONNECTION TIMED OUT";
            return;
        }
        while (wire.TakeDisconnected(out string lost))
        {
            if (IsHost)
                RemovePeer(lost);
            else if (lost == host)
                Error = "HOST DISCONNECTED";
            else if (addresses.TryGetValue(lost, out int peer))
                meshSeen.Remove(peer);
        }
        for (int n = 0; n < 4096 && wire.Receive(out var message); n++)
            Handle(message);
        if (Connected)
        {
            ConfigureMesh();
            LobbySession?.Poll();
            UpdateCheckpoint();
        }
        if (Now - lastSend >= 100)
        {
            lastSend = Now;
            if (IsHost)
            {
                RefreshSelections();
                foreach (var pair in addresses)
                    if (!SendStateIfChanged(pair.Value))
                        SendPeerControl(
                            pair.Value,
                            new Control
                            {
                                Kind = ControlKind.Heartbeat,
                                Nonce = nonce,
                                Generation = Generation,
                            },
                            false
                        );
            }
            else
                SendControl(
                    host!,
                    new Control
                    {
                        Kind = ControlKind.Hello,
                        Rollback = rollback,
                        Hash = contentHash,
                        Nonce = clientNonce,
                        Players = requested,
                        Spectating = requestedSpectating,
                        Invited = invited,
                        Version = requestVersion,
                        Epoch = requestEpoch,
                        Generation = Generation,
                        Mesh = ReadyLinks(),
                    }
                );
            if (Connected)
                foreach (int peer in RequiredPeers(LocalPeer))
                    SendPeerControl(
                        peer,
                        new Control
                        {
                            Kind = ControlKind.Mesh,
                            Nonce = nonce,
                            Peer = LocalPeer,
                        },
                        false
                    );
            SendCheckpointProgress();
        }
        foreach (var seen in lastSeen.ToArray())
        {
            if (Now - seen.Value <= 15000)
                continue;
            if (IsHost && peerAddresses.TryGetValue(seen.Key, out string? address))
                RemovePeer(address);
            else if (!IsHost && seen.Key == 0)
                Error = "HOST CONNECTION TIMED OUT";
        }
    }

    private void ConfigureMesh()
    {
        var required = RequiredPeers(LocalPeer).Select(peer => peerAddresses[peer]).ToArray();
        wire.SetPeers(required);
    }

    private int[] RequiredPeers(int peer)
    {
        if (peer == 0)
            return peerAddresses.Keys.Where(id => id != 0).ToArray();
        bool playing = Roster.Players(peer).Length > 0 || simulation?.InputSources.Any(p => p.Peer == peer) == true;
        if (!playing)
            return [0];
        return peerAddresses
            .Keys.Where(id =>
                id != peer
                && (id == 0 || Roster.Players(id).Length > 0 || simulation?.InputSources.Any(p => p.Peer == id) == true)
            )
            .ToArray();
    }

    private int[] ReadyLinks() =>
        RequiredPeers(LocalPeer)
            .Where(peer =>
                meshSeen.TryGetValue(peer, out long seen) && Now - seen < 3000 && wire.IsConnected(peerAddresses[peer])
            )
            .ToArray();

    private bool MeshReady(IEnumerable<int> participants)
    {
        var requiredParticipants = participants.ToHashSet();
        foreach (int peer in requiredParticipants)
        {
            int[] ready = peer == LocalPeer ? ReadyLinks() : meshReports.GetValueOrDefault(peer) ?? [];
            if (!RequiredPeers(peer).Where(requiredParticipants.Contains).All(ready.Contains))
                return false;
        }
        return true;
    }

    private Control State(int peer) =>
        new()
        {
            Kind = ControlKind.State,
            Rollbacks = peerRollback.Select(pair => new PeerRollback(pair.Key, pair.Value)).ToArray(),
            Peer = peer,
            Hash = contentHash,
            Nonce = nonce,
            ClientNonce = clientNonces[peer],
            Slots = Roster.Slots.ToArray(),
            Spectators = Roster.Spectators.ToArray(),
            Access = AccessFor(peer),
            Settings = MatchSettingsJson,
            Starting = Starting,
            Revision = revision,
            Generation = Generation,
            Session = SessionId,
            Version = versions[peer],
            Epoch = epochs[peer],
            Notice = Notice ?? "",
            Peers = peerAddresses.Select(pair => new PeerAddress(pair.Key, pair.Value)).ToArray(),
        };

    private bool SendStateIfChanged(int peer)
    {
        if (sentRevisions.TryGetValue(peer, out int sentRevision) && sentRevision == revision)
            return false;
        SendPeerControl(peer, State(peer));
        sentRevisions[peer] = revision;
        return true;
    }

    private void Handle(WireMessage message)
    {
        byte[] data = message.Data;
        if (data.Length is < 5 or > 8192 || BitConverter.ToUInt32(data) != Magic)
            return;
        if (data[4] == 1)
        {
            try
            {
                var control = JsonSerializer.Deserialize<Control>(data.AsSpan(5));
                if (control != null && Valid(control))
                    HandleControl(message.Source, control);
            }
            catch (Exception exception)
                when (exception is JsonException or ArgumentException or InvalidDataException or OverflowException) { }
            return;
        }
        if (data[4] != 2 || !Connected || data.Length < 15 || BitConverter.ToUInt64(data, 5) != SessionId)
            return;
        int origin = data[13],
            destination = data[14];
        if (
            destination != LocalPeer
            || origin == LocalPeer
            || !addresses.TryGetValue(message.Source, out int expected)
            || expected != origin
        )
            return;
        if (incoming.Count < 8192)
            incoming.Enqueue(new(origin, data[15..]));
    }

    private static bool Valid(Control control) =>
        Enum.IsDefined(control.Kind)
        && control.Rollback != null
        && control.Rollback.IsValid
        && control.Rollbacks != null
        && control.Rollbacks.Length <= LobbyRoster.MaxPeers
        && control.Rollbacks.All(item =>
            item != null
            && item.Peer is >= 0 and < LobbyRoster.MaxPeers
            && item.Settings != null
            && item.Settings.IsValid
        )
        && control.Hash != null
        && control.Nonce != null
        && control.ClientNonce != null
        && control.Settings != null
        && control.Notice != null
        && control.Players != null
        && control.Slots != null
        && control.Spectators != null
        && control.Peers != null
        && control.Mesh != null
        && control.Data != null
        && control.Inputs != null
        && control.Transaction != null
        && control.Spectators.Length <= LobbyRoster.MaxSpectators
        && control.Hash.Length <= 128
        && control.Nonce.Length <= 32
        && control.ClientNonce.Length <= 32
        && control.Settings.Length <= 4096
        && control.Players.Length <= 8
        && control.Players.All(player => player != null)
        && control.Slots.Length <= 8
        && control.Peers.Length <= LobbyRoster.MaxPeers
        && control.Peers.All(peer =>
            peer != null
            && peer.Address != null
            && peer.Address.Length <= 256
            && peer.Peer is >= 0 and < LobbyRoster.MaxPeers
        )
        && control.Mesh.Length <= LobbyRoster.MaxPeers
        && control.Mesh.All(peer => peer is >= 0 and < LobbyRoster.MaxPeers)
        && control.InputCount is >= 0 and <= SessionConfig.MaxInputs
        && control.Inputs.Length <= SessionConfig.MaxInputs
        && control.Inputs.All(input =>
            input != null
            && input.Peer is >= 0 and < LobbyRoster.MaxPeers
            && input.Id is >= -1 and < 10
            && (input.Id != -1 || input.Peer == 0)
            && input.Data != null
            && input.Data.Length <= RollbackPreferences.InputCapacityFrames * new RollbackInputCodec().Size
        )
        && control.Data.Length <= 1024
        && control.Transaction.Length <= 32
        && control.Version >= 0
        && control.Epoch >= 0
        && control.Generation >= 0;

    private void HandleControl(string source, Control control)
    {
        if (IsHost && control.Kind == ControlKind.Hello)
        {
            HandleHello(source, control);
            return;
        }
        if (control.Kind is ControlKind.Mesh or ControlKind.MeshAcknowledged)
        {
            if (
                !Connected
                || control.Nonce != nonce
                || !addresses.TryGetValue(source, out int peer)
                || peer != control.Peer
            )
                return;
            if (control.Kind == ControlKind.Mesh)
                SendControl(
                    source,
                    new Control
                    {
                        Kind = ControlKind.MeshAcknowledged,
                        Nonce = nonce,
                        Peer = LocalPeer,
                    },
                    false
                );
            else
                meshSeen[peer] = Now;
            return;
        }
        if (IsHost)
        {
            if (
                !addresses.TryGetValue(source, out int peer)
                || control.Nonce != nonce
                || control.Generation != Generation
            )
                return;
            lastSeen[peer] = Now;
            if (control.Kind == ControlKind.Leave)
                RemovePeer(source);
            else
                HandleCheckpointControl(peer, control);
            return;
        }
        if (source != host)
            return;
        if (control.Kind == ControlKind.Error && control.ClientNonce == clientNonce)
        {
            Error = control.Notice;
            return;
        }
        if (control.Kind != ControlKind.State)
        {
            if (Connected && control.Nonce == nonce && control.Generation == Generation)
            {
                lastSeen[0] = Now;
                HandleCheckpointControl(0, control);
            }
            return;
        }
        if (
            control.ClientNonce != clientNonce
            || control.Hash != contentHash
            || !Guid.TryParseExact(control.Nonce, "N", out _)
            || control.Peer is < 1 or >= LobbyRoster.MaxPeers
            || control.Revision < receivedRevision
            || control.Generation < Generation
        )
            return;
        if (Connected && control.Nonce != nonce)
            return;
        if (control.Generation > Generation)
        {
            Generation = control.Generation;
            SessionId = control.Session;
            phase = LobbyPhase.AwaitingSimulation;
            checkpoint = null;
            LobbySession?.Dispose();
            LobbySession = null;
            incoming.Clear();
        }
        Roster.Replace(control.Slots, control.Spectators);
        localAccess = control.Access;
        LocalPeer = control.Peer;
        nonce = control.Nonce;
        receivedRevision = control.Revision;
        MatchSettingsJson = control.Settings;
        Notice = control.Notice;
        InstallAddresses(control.Peers);
        peerRollback.Clear();
        foreach (var item in control.Rollbacks)
            if (peerAddresses.ContainsKey(item.Peer))
                peerRollback[item.Peer] = item.Settings;
        peerRollback[LocalPeer] = rollback;
        if (control.Version == requestVersion || control.Epoch != requestEpoch)
        {
            requestedSpectating = Roster.Spectator(LocalPeer) != null;
            requested = requestedSpectating ? [Roster.Spectator(LocalPeer)!] : Roster.Players(LocalPeer);
        }
        acknowledgedRequest = control.Epoch != requestEpoch ? requestVersion : control.Version;
        requestEpoch = control.Epoch;
        lastSeen[0] = Now;
    }

    private void InstallAddresses(PeerAddress[] peers)
    {
        if (
            peers.Select(p => p.Peer).Distinct().Count() != peers.Length
            || !peers.Any(p => p.Peer == 0)
            || !peers.Any(p => p.Peer == LocalPeer)
        )
            throw new InvalidDataException("Invalid mesh roster");
        var removedPeers = peerAddresses
            .Keys.Where(peer => peer != LocalPeer && peers.All(next => next.Peer != peer))
            .ToArray();
        foreach (int peer in removedPeers)
            LobbySession?.DisconnectPeer(peer);
        peerAddresses.Clear();
        addresses.Clear();
        foreach (var peer in peers)
        {
            string address = peer.Peer == 0 && !IsHost ? host! : peer.Address;
            peerAddresses.Add(peer.Peer, address);
            if (peer.Peer != LocalPeer)
                addresses.Add(address, peer.Peer);
        }
    }

    private void HandleHello(string source, Control control)
    {
        if (!Guid.TryParseExact(control.Nonce, "N", out _))
            return;
        if (rejectedClients.TryGetValue(source, out var rejected) && rejected.Nonce == control.Nonce)
        {
            Reject(source, rejected.Reason, control.Nonce);
            return;
        }
        if (control.Hash != contentHash)
        {
            Reject(source, "GAME VERSION MISMATCH", control.Nonce);
            return;
        }
        if (!addresses.TryGetValue(source, out int peer))
        {
            if (
                Starting
                || matchRequested
                || checkpoint != null
                || HasPendingSlotEdits
                || banned.Contains(source)
                || addresses.Count >= LobbyRoster.MaxPeers - 1
            )
            {
                if (checkpoint == null && !HasPendingSlotEdits)
                    Reject(source, "LOBBY UNAVAILABLE", control.Nonce);
                return;
            }
            RefreshSelections();
            peer = Enumerable.Range(1, LobbyRoster.MaxPeers - 1).First(id => !peerAddresses.ContainsKey(id));
            var permission = new LobbyAccess(control.Invited, isFriend(source));
            if (
                control.Spectating
                || control.Players.Length == 0
                || control.Players.Any(p => p.Cpu)
                || !Roster.SetPlayers(peer, control.Players, permission)
            )
            {
                Reject(source, "NOT ENOUGH OPEN SLOTS", control.Nonce);
                return;
            }
            addresses[source] = peer;
            peerAddresses[peer] = source;
            access[peer] = permission;
            clientNonces[peer] = control.Nonce;
            versions[peer] = control.Version;
            epochs[peer] = 0;
            Changed();
        }
        if (clientNonces[peer] != control.Nonce)
            return;
        if (peerRollback.GetValueOrDefault(peer) != control.Rollback)
        {
            peerRollback[peer] = control.Rollback;
            revision++;
        }
        lastSeen[peer] = Now;
        meshReports[peer] = control.Mesh;
        if (
            !Starting
            && checkpoint == null
            && !HasPendingSlotEdits
            && control.Generation == Generation
            && control.Version > versions[peer]
        )
        {
            if (control.Epoch == epochs[peer])
            {
                RefreshSelections();
                var next = CopyRoster();
                if (ApplyParty(next, peer, control.Players, control.Spectating, AccessFor(peer)))
                {
                    Roster.Replace(next.Slots, next.Spectators);
                    Notice = null;
                    Changed();
                }
                else
                {
                    Notice = control.Spectating ? "CANNOT SPECTATE" : "NO OPEN SLOTS";
                    PublishRoster();
                }
            }
            versions[peer] = control.Version;
        }
        SendStateIfChanged(peer);
    }

    private void RefreshSelections()
    {
        if (simulation == null || phase != LobbyPhase.Lobby || checkpoint != null)
            return;
        var live = simulation.Membership.Rooms.OfType<LobbyPlayer>().ToDictionary(p => (p.Peer, p.Id));
        var slots = Roster
            .Slots.Select(slot =>
                slot.Player is { } player && live.TryGetValue((player.Peer, player.Id), out var current)
                    ? slot with
                    {
                        Player = current,
                    }
                    : slot
            )
            .ToArray();
        if (
            slots.Where(slot => slot.Player != null).Select(slot => slot.Player!.Color).Distinct().Count()
            == Roster.Count
        )
            Roster.Replace(slots, Roster.Spectators);
    }

    private void FreezeRoster()
    {
        var players = Roster
            .Slots.Where(slot => slot.Player != null)
            .Select(slot => slot.Player!)
            .OrderBy(player => player.Peer)
            .ToArray();
        if (players.Length < 2 || players.Any(player => !player.Spawned))
            throw new InvalidDataException("SPAWN ALL PLAYERS");
        InputPlayerSlots =
        [
            -1,
            .. players
                .Select((player, slot) => (player, slot))
                .Where(item => !item.player.Cpu)
                .Select(item => item.slot),
        ];
        PeerSlots = Enumerable
            .Range(0, peerAddresses.Keys.Max() + 1)
            .Select(peer =>
                InputPlayerSlots
                    .Select((slot, handle) => (slot, handle))
                    .Where(item => (item.slot < 0 ? 0 : players[item.slot].Peer) == peer)
                    .Select(item => item.handle)
                    .ToArray()
            )
            .ToArray();
        PlayerTeams = players.Select(p => p.Team).ToArray();
        PlayerColors = players.Select(p => p.Color).ToArray();
    }

    private void Reject(string address, string reason, string requestNonce)
    {
        rejectedClients[address] = (requestNonce, reason);
        SendControl(
            address,
            new Control
            {
                Kind = ControlKind.Error,
                ClientNonce = requestNonce,
                Notice = reason,
            }
        );
    }

    private void SendPeerControl(int peer, Control control, bool reliable = true)
    {
        if (peerAddresses.TryGetValue(peer, out var address))
            SendControl(address, control, reliable);
    }

    private void SendControl(string address, Control control, bool reliable = true)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(control);
        byte[] data = new byte[json.Length + 5];
        BitConverter.TryWriteBytes(data, Magic);
        data[4] = 1;
        json.CopyTo(data, 5);
        wire.Send(address, data, reliable);
    }

    public IPeerTransport CreateTransport() =>
        Connected
            ? new SessionTransport(this, SessionId)
            : throw new InvalidOperationException("Lobby is not connected");

    private sealed class SessionTransport(MeshLobby lobby, ulong session) : IPeerTransport
    {
        public string? Error => lobby.Error;
        public long TimeMilliseconds => lobby.Now;

        public void Send(int peer, ReadOnlySpan<byte> data)
        {
            if (session == lobby.SessionId)
                lobby.Send(peer, data);
        }

        public bool TryReceive(out Datagram datagram)
        {
            datagram = default;
            return session == lobby.SessionId && lobby.TryReceive(out datagram);
        }

        public void Dispose() { }
    }

    public void Send(int peer, ReadOnlySpan<byte> data)
    {
        if (!Connected || peer == LocalPeer || !peerAddresses.TryGetValue(peer, out var address) || data.Length > 1200)
            return;
        byte[] packet = new byte[data.Length + 15];
        BitConverter.TryWriteBytes(packet, Magic);
        packet[4] = 2;
        BitConverter.TryWriteBytes(packet.AsSpan(5), SessionId);
        packet[13] = (byte)LocalPeer;
        packet[14] = (byte)peer;
        data.CopyTo(packet.AsSpan(15));
        wire.Send(address, packet, false);
    }

    public bool TryReceive(out Datagram datagram) => incoming.TryDequeue(out datagram);

    public void Dispose()
    {
        if (disposed)
            return;
        if (!IsHost && Connected)
            SendControl(
                host!,
                new Control
                {
                    Kind = ControlKind.Leave,
                    Nonce = nonce,
                    Generation = Generation,
                }
            );
        disposed = true;
        LobbySession?.Dispose();
        wire.Dispose();
    }

    public sealed record PeerAddress(int Peer, string Address);

    public sealed record PendingInput(int Peer, int Id, byte[] Data);

    public sealed record PeerRollback(int Peer, RollbackPreferences Settings);

    public sealed record Control
    {
        public RollbackPreferences Rollback { get; set; } = new();
        public PeerRollback[] Rollbacks { get; set; } = [];
        public int InputCount { get; set; }
        public ControlKind Kind { get; set; }
        public string Hash { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string ClientNonce { get; set; } = "";
        public LobbyPlayer[] Players { get; set; } = [];
        public LobbySlot[] Slots { get; set; } = [];
        public LobbyPlayer[] Spectators { get; set; } = [];
        public PeerAddress[] Peers { get; set; } = [];
        public int[] Mesh { get; set; } = [];
        public bool Spectating { get; set; }
        public bool Invited { get; set; }
        public LobbyAccess Access { get; set; }
        public int Peer { get; set; }
        public int Version { get; set; }
        public int Epoch { get; set; }
        public int Revision { get; set; }
        public int Generation { get; set; }
        public ulong Session { get; set; }
        public bool Starting { get; set; }
        public string Settings { get; set; } = "";
        public string Notice { get; set; } = "";
        public string Transaction { get; set; } = "";
        public int Change { get; set; }
        public long Tick { get; set; }
        public int Size { get; set; }
        public int Chunk { get; set; }
        public byte[] Data { get; set; } = [];
        public PendingInput[] Inputs { get; set; } = [];
    }
}
