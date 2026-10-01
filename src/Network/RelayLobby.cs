using System.Text.Json;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

internal sealed class RelayLobby : IGameLobby, IPeerTransport
{
    private const uint Magic = 0x46534C32;
    private readonly IWire wire;
    private readonly string? host;
    private readonly string contentHash;
    private string nonce = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, int> addresses = new();
    private readonly Dictionary<int, string> clientNonces = new();
    private readonly Dictionary<int, int> versions = new();
    private readonly Dictionary<int, int> epochs = new();
    private readonly HashSet<string> banned = new();
    private readonly HashSet<int> acknowledged = new();
    private readonly HashSet<int> started = new();
    private readonly Queue<Datagram> incoming = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<int, long> lastSeen = new();
    private readonly InputFrame[] lobbyInputs = new InputFrame[8];
    private readonly long[] inputTimes = new long[8];
    private readonly Dictionary<int, long> inputSequences = new();
    private LobbyPlayer[] requested;
    private int requestVersion;
    private int requestEpoch;
    private int revision;
    private int receivedRevision = -1;
    private long lastSend = -1000;
    private long inputSequence;
    private long snapshotSequence;
    private long receivedSnapshot = -1;
    private byte[]? latestSnapshot;
    private bool startSent;
    private bool disposed;
    private string acceptedNonce = "";
    public bool IsHost => host == null;
    public bool Connected => LocalPeer >= 0;
    public bool Starting { get; private set; }
    public bool Ready { get; private set; }
    public int Generation { get; private set; }
    public string Status =>
        Ready ? "Connected"
        : Starting ? "Starting match"
        : Connected ? $"{Roster.Count}/{Roster.Capacity} players"
        : "Connecting";
    public string? Error { get; private set; }
    public string? Notice { get; private set; }
    public int LocalPeer { get; private set; } = -1;
    public LobbyRoster Roster { get; } = new();
    public int[][] PeerSlots { get; private set; } = [];
    public int[] PlayerTeams { get; private set; } = [];
    public int[] PlayerColors { get; private set; } = [];
    public string MatchSettingsJson { get; private set; }

    public RelayLobby(
        IWire wire,
        string? host,
        int capacity,
        LobbyPlayer[] players,
        string hash,
        string settings,
        IReadOnlyList<LobbySlot>? initialRooms = null
    )
    {
        this.wire = wire;
        this.host = host;
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
        }
        else
            Roster.SetPlayers(0, []);
    }

    public bool SetPlayers(LobbyPlayer[] players)
    {
        if (!Connected || Starting)
            return false;
        if (IsHost)
        {
            if (!Roster.SetPlayers(0, players))
                return false;
            Changed();
        }
        else
        {
            var validation = new LobbyRoster();
            if (!validation.SetPlayers(LocalPeer, players))
                return false;
            requested = players.ToArray();
            requestVersion++;
            lastSend = -1000;
        }
        return true;
    }

    public bool EditSlot(int room, SlotType type, bool open, bool remove = false)
    {
        if (!IsHost || Starting || room is < 0 or > 7)
            return false;
        int peer = Roster.Slots[room].Player?.Peer ?? 0;
        if (!Roster.Edit(room, type, open, remove))
            return false;
        if (peer > 0 && Roster.Players(peer).Length == 0)
        {
            Kick(peer, false);
            return true;
        }
        if (peer > 0)
            epochs[peer]++;
        Changed();
        return true;
    }

    private void Changed()
    {
        revision++;
        lastSend = -1000;
    }

    public void Kick(int peer, bool ban)
    {
        if (!IsHost || Starting || peer <= 0)
            return;
        string? address = addresses.FirstOrDefault(pair => pair.Value == peer).Key;
        if (address == null)
            return;
        if (ban)
            banned.Add(address);
        Reject(
            address,
            ban ? "The host banned this connection from the lobby" : "The host removed this connection from the lobby",
            clientNonces[peer]
        );
        RemovePeer(address);
    }

    public void RemovePeer(string address)
    {
        if (!IsHost || !addresses.Remove(address, out int peer))
            return;
        if (Starting)
        {
            ReturnToLobby();
            Notice = "PLAYER LEFT";
        }
        Roster.SetPlayers(peer, []);
        clientNonces.Remove(peer);
        epochs.Remove(peer);
        versions.Remove(peer);
        lastSeen.Remove(peer);
        inputSequences.Remove(peer);
        Changed();
    }

    public void SetMatchSettings(string settings)
    {
        if (!IsHost || Starting || settings.Length > 4096 || settings == MatchSettingsJson)
            return;
        MatchSettingsJson = settings;
        Changed();
    }

    public bool StartMatch(string settings)
    {
        if (!IsHost || Starting || Roster.Count < 2 || settings.Length > 4096)
            return false;
        if (Roster.Slots.Any(slot => slot.Player is { Spawned: false }))
        {
            Notice = "SPAWN ALL PLAYERS";
            return false;
        }
        if (Enumerable.Prepend(addresses.Values, 0).Any(peer => Roster.Players(peer).Length == 0))
        {
            Notice = "JOIN A PLAYER ON EACH PC";
            return false;
        }
        MatchSettingsJson = settings;
        var oldPeers = addresses.Values.Prepend(0).Order().ToArray();
        var remap = oldPeers.Select((peer, index) => (peer, index)).ToDictionary(pair => pair.peer, pair => pair.index);
        Roster.Replace(
            Roster
                .Slots.Select(slot =>
                    slot.Player == null
                        ? slot
                        : slot with
                        {
                            Player = slot.Player with { Peer = remap[slot.Player.Peer] },
                        }
                )
                .ToArray()
        );
        var oldNonces = new Dictionary<int, string>(clientNonces);
        var oldVersions = new Dictionary<int, int>(versions);
        var oldEpochs = new Dictionary<int, int>(epochs);
        lastSeen.Clear();
        foreach (string address in addresses.Keys.ToArray())
        {
            int old = addresses[address];
            int peer = addresses[address] = remap[old];
            clientNonces[peer] = oldNonces[old];
            versions[peer] = oldVersions[old];
            epochs[peer] = oldEpochs[old];
            lastSeen[peer] = clock.ElapsedMilliseconds;
        }
        Starting = true;
        FreezeRoster();
        Changed();
        if (addresses.Count == 0)
            Ready = true;
        return true;
    }

    public bool ReturnToLobby()
    {
        if (!IsHost || !Starting || Error != null)
            return false;
        Generation++;
        nonce = Guid.NewGuid().ToString("N");
        ResetMatch();
        foreach (int peer in addresses.Values)
        {
            epochs[peer]++;
            lastSeen[peer] = clock.ElapsedMilliseconds;
        }
        Changed();
        return true;
    }

    private void ResetMatch()
    {
        Starting = Ready = startSent = false;
        acknowledged.Clear();
        started.Clear();
        incoming.Clear();
        PeerSlots = [];
        PlayerTeams = PlayerColors = [];
        Array.Clear(lobbyInputs);
        Array.Clear(inputTimes);
        inputSequences.Clear();
        inputSequence = snapshotSequence = 0;
        receivedSnapshot = -1;
        latestSnapshot = null;
        Notice = null;
        lastSend = -1000;
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
        int count = 0;
        while (count++ < 512 && wire.Receive(out var message))
            Handle(message);
        if (clock.ElapsedMilliseconds - lastSend >= 100)
        {
            lastSend = clock.ElapsedMilliseconds;
            if (!IsHost)
                SendControl(
                    host!,
                    new Control
                    {
                        Kind = Starting
                            ? Ready
                                ? "started"
                                : "ack"
                            : "hello",
                        Hash = contentHash,
                        Nonce = Starting ? acceptedNonce : nonce,
                        Players = requested,
                        Version = requestVersion,
                        Epoch = requestEpoch,
                        Revision = receivedRevision,
                        Generation = Generation,
                    }
                );
            else
                foreach (var pair in addresses)
                    SendControl(pair.Key, State(pair.Value));
        }
        foreach (var seen in lastSeen.ToArray())
        {
            if (clock.ElapsedMilliseconds - seen.Value <= 15000)
                continue;
            if (IsHost)
                RemovePeer(addresses.First(pair => pair.Value == seen.Key).Key);
            else
                Error = "A connection timed out";
        }
        if (!Connected && clock.Elapsed.TotalSeconds > 30)
            Error = "Lobby connection timed out";
    }

    private Control State(int peer) =>
        new()
        {
            Kind = startSent ? "start" : "state",
            Peer = peer,
            Hash = contentHash,
            Nonce = nonce,
            ClientNonce = clientNonces[peer],
            Slots = Roster.Slots.ToArray(),
            Settings = MatchSettingsJson,
            Starting = Starting,
            Revision = revision,
            Generation = Generation,
            Version = versions[peer],
            Epoch = epochs[peer],
            Notice = Notice ?? "",
        };

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
            catch (JsonException) { }
            catch (ArgumentException) { }
            return;
        }
        if (
            !Connected
            || data.Length < 23
            || !data.AsSpan(5, 16).SequenceEqual(Guid.ParseExact(IsHost ? nonce : acceptedNonce, "N").ToByteArray())
        )
            return;
        if (data[4] == 2 && Starting)
        {
            int origin = data[21],
                destination = data[22];
            if (origin >= PeerSlots.Length || destination >= PeerSlots.Length || origin == destination)
                return;
            if (IsHost && addresses.TryGetValue(message.Source, out int peer) && peer == origin)
            {
                lastSeen[peer] = clock.ElapsedMilliseconds;
                if (destination == 0)
                    incoming.Enqueue(new(origin, data[23..]));
                else
                    wire.Send(addresses.Single(pair => pair.Value == destination).Key, data, false);
            }
            else if (!IsHost && message.Source == host && destination == LocalPeer)
            {
                lastSeen[0] = clock.ElapsedMilliseconds;
                incoming.Enqueue(new(origin, data[23..]));
            }
        }
        else if (!Starting && data.Length >= 29)
            HandleLobbyData(message.Source, data);
    }

    private static bool Valid(Control control) =>
        control.Kind != null
        && control.Hash != null
        && control.Nonce != null
        && control.ClientNonce != null
        && control.Settings != null
        && control.Notice != null
        && control.Players != null
        && control.Slots != null
        && control.Hash.Length <= 128
        && control.Nonce.Length <= 32
        && control.ClientNonce.Length <= 32
        && control.Settings.Length <= 4096
        && control.Players.Length <= 8
        && control.Players.All(player => player != null)
        && control.Slots.Length <= 8
        && control.Version >= 0
        && control.Epoch >= 0
        && control.Generation >= 0;

    private void HandleControl(string source, Control control)
    {
        if (IsHost)
        {
            if (control.Kind == "hello")
                HandleHello(source, control);
            else if (
                addresses.TryGetValue(source, out int peer)
                && control.Nonce == nonce
                && control.Generation == Generation
            )
            {
                lastSeen[peer] = clock.ElapsedMilliseconds;
                if (control.Kind == "leave")
                    RemovePeer(source);
                if (!Starting || control.Revision != revision)
                    return;
                if (control.Kind is "ack" or "started")
                    acknowledged.Add(peer);
                if (control.Kind == "started")
                    started.Add(peer);
                if (acknowledged.Count == addresses.Count && !startSent)
                {
                    startSent = true;
                    lastSend = -1000;
                }
                if (started.Count == addresses.Count)
                    Ready = true;
            }
            return;
        }
        if (source != host || control.ClientNonce != nonce)
            return;
        if (control.Kind == "error")
        {
            Error = control.Notice;
            return;
        }
        if (
            control.Kind is not ("state" or "start")
            || control.Hash != contentHash
            || !Guid.TryParseExact(control.Nonce, "N", out _)
            || control.Peer is < 1 or > 7
            || control.Revision < receivedRevision
            || control.Generation < Generation
        )
            return;
        if (Connected && control.Generation == Generation && control.Nonce != acceptedNonce)
            return;
        if (control.Generation > Generation)
        {
            ResetMatch();
            Generation = control.Generation;
        }
        if (Starting && (!control.Starting || control.Revision != receivedRevision))
        {
            Error = "Host changed the agreed match configuration";
            return;
        }
        Roster.Replace(control.Slots);
        LocalPeer = control.Peer;
        acceptedNonce = control.Nonce;
        receivedRevision = control.Revision;
        MatchSettingsJson = control.Settings;
        Notice = control.Notice;
        if (control.Version == requestVersion || control.Epoch != requestEpoch)
            requested = Roster.Players(LocalPeer);
        requestEpoch = control.Epoch;
        lastSeen[0] = clock.ElapsedMilliseconds;
        if (control.Starting && !Starting)
        {
            Starting = true;
            lastSend = -1000;
            FreezeRoster();
        }
        if (control.Kind == "start" && Starting && !Ready)
        {
            Ready = true;
            lastSend = -1000;
        }
    }

    private void HandleHello(string source, Control control)
    {
        if (!Guid.TryParseExact(control.Nonce, "N", out _))
            return;
        if (control.Hash != contentHash)
        {
            Reject(source, "Content/build fingerprint mismatch", control.Nonce);
            return;
        }
        if (!addresses.TryGetValue(source, out int peer))
        {
            if (Starting || banned.Contains(source) || addresses.Count >= 7)
            {
                Reject(source, "Lobby is unavailable or the match has started", control.Nonce);
                return;
            }
            peer = Enumerable.Range(1, 7).First(id => !addresses.ContainsValue(id));
            if (!Roster.SetPlayers(peer, control.Players))
            {
                Reject(source, "Not enough open slots for your local players", control.Nonce);
                return;
            }
            addresses[source] = peer;
            clientNonces[peer] = control.Nonce;
            versions[peer] = control.Version;
            epochs[peer] = 0;
            Changed();
        }
        if (clientNonces[peer] != control.Nonce)
            return;
        lastSeen[peer] = clock.ElapsedMilliseconds;
        if (control.Generation != Generation)
        {
            SendControl(source, State(peer));
            return;
        }
        if (!Starting && control.Version > versions[peer])
        {
            if (control.Epoch == epochs[peer])
            {
                Notice = Roster.SetPlayers(peer, control.Players) ? null : "NO OPEN SLOTS";
                Changed();
            }
            versions[peer] = control.Version;
        }
        SendControl(source, State(peer));
    }

    private void FreezeRoster()
    {
        var players = Roster
            .Slots.Where(slot => slot.Player != null)
            .Select(slot => slot.Player!)
            .OrderBy(player => player.Peer)
            .ToArray();
        if (players.Length < 2 || players.Any(player => !player.Spawned))
            throw new ArgumentException("Invalid match roster");
        int peers = players.Max(player => player.Peer) + 1;
        PeerSlots = Enumerable
            .Range(0, peers)
            .Select(peer =>
                players
                    .Select((player, index) => (player, index))
                    .Where(item => item.player.Peer == peer)
                    .Select(item => item.index)
                    .ToArray()
            )
            .ToArray();
        if (PeerSlots.Any(slots => slots.Length == 0))
            throw new ArgumentException("Empty match peer");
        PlayerTeams = players.Select(player => player.Team).ToArray();
        PlayerColors = players.Select(player => player.Color).ToArray();
    }

    private void Reject(string address, string reason, string clientNonce) =>
        SendControl(
            address,
            new Control
            {
                Kind = "error",
                ClientNonce = clientNonce,
                Notice = reason,
            }
        );

    private void SendControl(string address, Control control)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(control);
        byte[] data = new byte[json.Length + 5];
        BitConverter.TryWriteBytes(data, Magic);
        data[4] = 1;
        json.CopyTo(data, 5);
        wire.Send(address, data, true);
    }

    private byte[] Packet(byte kind, int size)
    {
        byte[] data = new byte[size];
        BitConverter.TryWriteBytes(data, Magic);
        data[4] = kind;
        Guid.ParseExact(IsHost ? nonce : acceptedNonce, "N").ToByteArray().CopyTo(data, 5);
        return data;
    }

    public IPeerTransport CreateTransport() =>
        Ready ? new SessionTransport(this, Generation) : throw new InvalidOperationException("Lobby is not ready");

    private sealed class SessionTransport(RelayLobby lobby, int generation) : IPeerTransport
    {
        public string? Error => lobby.Error;

        public void Poll() => lobby.Poll();

        public void Send(int peer, ReadOnlySpan<byte> data)
        {
            if (generation == lobby.Generation)
                lobby.Send(peer, data);
        }

        public bool TryReceive(out Datagram datagram)
        {
            datagram = default;
            return generation == lobby.Generation && lobby.TryReceive(out datagram);
        }

        public void Dispose() { }
    }

    public void Send(int peer, ReadOnlySpan<byte> data)
    {
        if (!Ready || peer == LocalPeer || peer < 0 || peer >= PeerSlots.Length || data.Length > 1200)
            return;
        byte[] packet = Packet(2, data.Length + 23);
        packet[21] = (byte)LocalPeer;
        packet[22] = (byte)peer;
        data.CopyTo(packet.AsSpan(23));
        wire.Send(host ?? addresses.Single(pair => pair.Value == peer).Key, packet, false);
    }

    public bool TryReceive(out Datagram datagram) => incoming.TryDequeue(out datagram);

    public void SendLobbyInputs(InputFrame[] inputs)
    {
        if (IsHost || !Connected || Starting || inputs.Length != 8)
            return;
        byte[] packet = Packet(3, 61);
        BitConverter.TryWriteBytes(packet.AsSpan(21), ++inputSequence);
        for (int room = 0; room < 8; room++)
            BitConverter.TryWriteBytes(packet.AsSpan(29 + room * 4), inputs[room].Packed);
        wire.Send(host!, packet, false);
    }

    private void HandleLobbyData(string source, byte[] data)
    {
        long sequence = BitConverter.ToInt64(data, 21);
        if (data[4] == 3 && IsHost && data.Length == 61 && addresses.TryGetValue(source, out int peer))
        {
            if (sequence <= inputSequences.GetValueOrDefault(peer))
                return;
            var inputs = new InputFrame[8];
            try
            {
                for (int room = 0; room < 8; room++)
                    inputs[room] = InputFrame.FromPacked(BitConverter.ToUInt32(data, 29 + room * 4));
            }
            catch (InvalidDataException)
            {
                return;
            }
            inputSequences[peer] = sequence;
            for (int room = 0; room < 8; room++)
            {
                if (Roster.Slots[room].Player?.Peer != peer)
                    continue;
                lobbyInputs[room] = inputs[room];
                inputTimes[room] = clock.ElapsedMilliseconds;
            }
        }
        else if (data[4] == 4 && !IsHost && source == host && sequence > receivedSnapshot)
        {
            latestSnapshot = data[29..];
            receivedSnapshot = sequence;
        }
    }

    public InputFrame[] ReadLobbyInputs() =>
        lobbyInputs
            .Select((input, room) => clock.ElapsedMilliseconds - inputTimes[room] < 300 ? input : default)
            .ToArray();

    public void SendSnapshot(byte[] snapshot)
    {
        if (!IsHost || Starting || snapshot.Length > 8163)
            return;
        byte[] data = Packet(4, snapshot.Length + 29);
        BitConverter.TryWriteBytes(data.AsSpan(21), ++snapshotSequence);
        snapshot.CopyTo(data, 29);
        foreach (string address in addresses.Keys)
            wire.Send(address, data, false);
    }

    public byte[]? TakeSnapshot()
    {
        byte[]? snapshot = latestSnapshot;
        latestSnapshot = null;
        return snapshot;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        if (!IsHost && Connected)
            SendControl(
                host!,
                new Control
                {
                    Kind = "leave",
                    Nonce = acceptedNonce,
                    Generation = Generation,
                }
            );
        disposed = true;
        wire.Dispose();
    }

    public sealed class Control
    {
        public string Kind { get; set; } = "";
        public string Hash { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string ClientNonce { get; set; } = "";
        public LobbyPlayer[] Players { get; set; } = [];
        public LobbySlot[] Slots { get; set; } = [];
        public int Peer { get; set; }
        public int Version { get; set; }
        public int Epoch { get; set; }
        public int Revision { get; set; }
        public int Generation { get; set; }
        public bool Starting { get; set; }
        public string Settings { get; set; } = "";
        public string Notice { get; set; } = "";
    }
}
