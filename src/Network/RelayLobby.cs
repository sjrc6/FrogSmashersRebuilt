using System.Text.Json;

namespace FrogSmashers.Network;

internal sealed class RelayLobby : IGameLobby, IPeerTransport
{
    private const uint Magic = 0x46534C31;
    private readonly IWire wire;
    private readonly string? host;
    private readonly int expectedPeers;
    private readonly int localPlayers;
    private readonly string contentHash;
    private readonly int[] localTeams;
    private readonly string nonce = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, int> addresses = new();
    private readonly Dictionary<int, int> counts = new();
    private readonly Dictionary<int, int[]> teams = new();
    private readonly Dictionary<int, string> clientNonces = new();
    private readonly HashSet<int> acknowledgedPeers = new();
    private readonly HashSet<int> startedPeers = new();
    private readonly Queue<Datagram> incoming = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<int, long> lastSeen = new();
    private long lastSend = -1000;
    private bool rosterFrozen;
    private bool startSent;
    private bool disposed;
    private string acceptedNonce = "";
    public bool Ready { get; private set; }
    public string Status { get; private set; } = "Connecting";
    public string? Error { get; private set; }
    public int LocalPeer { get; private set; } = -1;
    public int[][] PeerSlots { get; private set; } = [];
    public int[] PlayerTeams { get; private set; } = [];
    public string MatchSettingsJson { get; private set; }

    public RelayLobby(
        IWire wire,
        string? host,
        int peers,
        int players,
        string hash,
        string settings,
        int[]? localTeams = null
    )
    {
        if (players is < 1 or > 7 || (host == null && (peers is < 2 or > 8 || players + peers - 1 > 8)))
        {
            throw new ArgumentException("Invalid lobby size");
        }

        this.localTeams = localTeams?.ToArray() ?? [];
        if (
            this.localTeams.Length != 0 && this.localTeams.Length != players
            || this.localTeams.Any(t => t is < -1 or > 7)
        )
        {
            throw new ArgumentException("Invalid local team selections");
        }

        this.wire = wire;
        this.host = host;
        expectedPeers = peers;
        localPlayers = players;
        contentHash = hash;
        MatchSettingsJson = settings;
        if (settings.Length > 4096 || hash.Length > 128)
        {
            throw new ArgumentException("Lobby settings too large");
        }

        if (host == null)
        {
            LocalPeer = 0;
            counts[0] = players;
            teams[0] = this.localTeams;
            Status = $"Waiting for {peers - 1} peer(s)";
        }
    }

    public void Poll()
    {
        if (disposed || Error != null)
        {
            return;
        }

        wire.Poll();
        if (wire.Error != null)
        {
            Error = wire.Error;
            return;
        }

        int count = 0;
        while (count++ < 512 && wire.Receive(out var message))
        {
            Handle(message);
        }

        if (Error != null)
        {
            return;
        }

        if (clock.ElapsedMilliseconds - lastSend >= 100)
        {
            lastSend = clock.ElapsedMilliseconds;
            if (host != null)
            {
                if (!rosterFrozen)
                {
                    SendControl(
                        host,
                        new Control
                        {
                            Kind = LobbyMessageKind.Hello,
                            Players = localPlayers,
                            Teams = localTeams,
                            Hash = contentHash,
                            Nonce = nonce,
                        }
                    );
                }
                else
                {
                    SendControl(
                        host,
                        new Control
                        {
                            Kind = Ready ? LobbyMessageKind.Started : LobbyMessageKind.Acknowledge,
                            Nonce = acceptedNonce,
                        }
                    );
                }
            }
            else if (rosterFrozen)
            {
                foreach (var address in addresses)
                {
                    SendControl(
                        address.Key,
                        startSent
                            ? new Control { Kind = LobbyMessageKind.Start, Nonce = nonce }
                            : Welcome(address.Value)
                    );
                }
            }
        }

        if (Ready)
        {
            foreach (var seen in lastSeen)
            {
                if (clock.ElapsedMilliseconds - seen.Value > 15000)
                {
                    Error = $"Peer {seen.Key} disconnected (15 second timeout)";
                }
            }
        }
        else if (clock.ElapsedMilliseconds > 120000)
        {
            Error = "Lobby connection timed out";
        }
    }

    private Control Welcome(int peer) =>
        new()
        {
            Kind = LobbyMessageKind.Welcome,
            Peer = peer,
            Counts = counts.OrderBy(x => x.Key).Select(x => x.Value).ToArray(),
            Hash = contentHash,
            Teams = PlayerTeams,
            Settings = MatchSettingsJson,
            Nonce = nonce,
            ClientNonce = clientNonces[peer],
        };

    private void Handle(WireMessage message)
    {
        if (message.Data.Length is < 5 or > 8192 || BitConverter.ToUInt32(message.Data) != Magic)
        {
            return;
        }

        byte kind = message.Data[4];
        if (kind == 1)
        {
            try
            {
                var control = JsonSerializer.Deserialize<Control>(message.Data.AsSpan(5));
                if (
                    control == null
                    || control.Hash == null
                    || control.Nonce == null
                    || control.ClientNonce == null
                    || control.Settings == null
                    || control.Counts == null
                    || control.Teams == null
                    || control.Hash.Length > 128
                    || control.Nonce.Length > 32
                    || control.ClientNonce.Length > 32
                    || control.Settings.Length > 4096
                    || control.Counts.Length > 8
                    || control.Teams.Length > 8
                    || control.Teams.Any(t => t is < -1 or > 7)
                )
                {
                    return;
                }

                HandleControl(message.Source, control);
            }
            catch (JsonException) { }
        }
        else if (kind == 2 && rosterFrozen && message.Data.Length >= 23)
        {
            if (
                !message
                    .Data.AsSpan(5, 16)
                    .SequenceEqual(Guid.ParseExact(host == null ? nonce : acceptedNonce, "N").ToByteArray())
            )
            {
                return;
            }

            int origin = message.Data[21];
            int destination = message.Data[22];
            if (origin < 0 || origin >= PeerSlots.Length || destination >= PeerSlots.Length || origin == destination)
            {
                return;
            }

            if (host == null)
            {
                if (!addresses.TryGetValue(message.Source, out int owner) || owner != origin)
                {
                    return;
                }

                lastSeen[owner] = clock.ElapsedMilliseconds;
                if (destination == 0)
                {
                    incoming.Enqueue(new Datagram(origin, message.Data[23..]));
                }
                else
                {
                    var target = addresses.FirstOrDefault(x => x.Value == destination).Key;
                    if (target != null)
                    {
                        wire.Send(target, message.Data, false);
                    }
                }
            }
            else if (message.Source == host && destination == LocalPeer)
            {
                lastSeen[0] = clock.ElapsedMilliseconds;
                incoming.Enqueue(new Datagram(origin, message.Data[23..]));
            }
        }
    }

    private void HandleControl(string source, Control control)
    {
        if (host == null)
        {
            if (control.Kind == LobbyMessageKind.Hello)
            {
                HandleHello(source, control);
            }
            else if (addresses.TryGetValue(source, out int peer) && control.Nonce == nonce)
            {
                HandleAcknowledgement(peer, control);
            }

            return;
        }

        if (source != host)
        {
            return;
        }

        if (control.Kind == LobbyMessageKind.Error)
        {
            Error = control.Settings;
            return;
        }

        if (control.Kind == LobbyMessageKind.Welcome)
        {
            if (!HandleWelcome(control))
            {
                return;
            }
        }
        else if (control.Kind == LobbyMessageKind.Start && rosterFrozen && control.Nonce == acceptedNonce)
        {
            Ready = true;
            Status = "Connected";
            SendControl(host, new Control { Kind = LobbyMessageKind.Started, Nonce = acceptedNonce });
        }

        lastSeen[0] = clock.ElapsedMilliseconds;
    }

    private void HandleHello(string source, Control control)
    {
        if (control.Hash != contentHash)
        {
            SendControl(
                source,
                new Control { Kind = LobbyMessageKind.Error, Settings = "Content/build fingerprint mismatch" }
            );
            return;
        }

        if (
            control.Players is < 1 or > 7
            || !Guid.TryParseExact(control.Nonce, "N", out _)
            || control.Teams.Length != 0 && control.Teams.Length != control.Players
        )
        {
            return;
        }

        if (!addresses.TryGetValue(source, out int peer))
        {
            if (rosterFrozen || counts.Count >= expectedPeers)
            {
                SendControl(
                    source,
                    new Control { Kind = LobbyMessageKind.Error, Settings = "Lobby is full or already started" }
                );
                return;
            }

            if (counts.Values.Sum() + control.Players + (expectedPeers - counts.Count - 1) > 8)
            {
                SendControl(
                    source,
                    new Control { Kind = LobbyMessageKind.Error, Settings = "A match supports at most eight players" }
                );
                return;
            }

            peer = counts.Count;
            addresses[source] = peer;
            counts[peer] = control.Players;
            teams[peer] = control.Teams.ToArray();
            clientNonces[peer] = control.Nonce;
            lastSeen[peer] = clock.ElapsedMilliseconds;
        }

        if (
            clientNonces[peer] != control.Nonce
            || counts[peer] != control.Players
            || !teams[peer].SequenceEqual(control.Teams)
        )
        {
            return;
        }

        Status = $"Connected {counts.Count}/{expectedPeers} peers";
        if (counts.Count == expectedPeers && !rosterFrozen)
        {
            FreezeRoster(counts.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray(), SelectTeams());
        }

        if (rosterFrozen)
        {
            SendControl(source, Welcome(peer));
        }
    }

    private int[] SelectTeams()
    {
        var selected = new List<int>();
        foreach (var peer in counts.OrderBy(pair => pair.Key))
        {
            var requested = teams[peer.Key];
            int offset = selected.Count;
            for (int localSlot = 0; localSlot < peer.Value; localSlot++)
            {
                int team = requested.Length == 0 ? -1 : requested[localSlot];
                selected.Add(team < 0 ? (offset + localSlot) % 2 : team);
            }
        }

        return selected.ToArray();
    }

    private void HandleAcknowledgement(int peer, Control control)
    {
        lastSeen[peer] = clock.ElapsedMilliseconds;
        if (control.Kind == LobbyMessageKind.Acknowledge)
        {
            acknowledgedPeers.Add(peer);
        }

        if (control.Kind == LobbyMessageKind.Started)
        {
            acknowledgedPeers.Add(peer);
            startedPeers.Add(peer);
        }

        if (acknowledgedPeers.Count == expectedPeers - 1 && !startSent)
        {
            startSent = true;
            Status = "Starting match";
            foreach (var address in addresses.Keys)
            {
                SendControl(address, new Control { Kind = LobbyMessageKind.Start, Nonce = nonce });
            }
        }

        if (startedPeers.Count == expectedPeers - 1)
        {
            Ready = true;
            Status = "Connected";
        }
    }

    private bool HandleWelcome(Control control)
    {
        if (
            control.Hash != contentHash
            || control.ClientNonce != nonce
            || control.Counts.Length is < 2 or > 8
            || control.Peer <= 0
            || control.Peer >= control.Counts.Length
            || control.Counts[control.Peer] != localPlayers
            || control.Counts.Any(count => count is < 1 or > 8)
            || control.Counts.Sum() > 8
            || control.Teams.Length != control.Counts.Sum()
            || control.Teams.Any(team => team < 0)
            || !Guid.TryParseExact(control.Nonce, "N", out _)
        )
        {
            return false;
        }

        if (
            rosterFrozen
            && (
                control.Nonce != acceptedNonce
                || MatchSettingsJson != control.Settings
                || control.Peer != LocalPeer
                || !control.Counts.SequenceEqual(PeerSlots.Select(slots => slots.Length))
                || !control.Teams.SequenceEqual(PlayerTeams)
            )
        )
        {
            Error = "Host changed the agreed match configuration";
            return false;
        }

        int localOffset = control.Counts.Take(control.Peer).Sum();
        for (int localSlot = 0; localSlot < localPlayers; localSlot++)
        {
            int team = localTeams.Length == 0 ? -1 : localTeams[localSlot];
            int expectedTeam = team < 0 ? (localOffset + localSlot) % 2 : team;
            if (expectedTeam != control.Teams[localOffset + localSlot])
            {
                Error = "Host changed local team selections";
                return false;
            }
        }

        LocalPeer = control.Peer;
        acceptedNonce = control.Nonce;
        MatchSettingsJson = control.Settings;
        FreezeRoster(control.Counts, control.Teams);
        Status = "Synchronizing match start";
        SendControl(host!, new Control { Kind = LobbyMessageKind.Acknowledge, Nonce = acceptedNonce });
        return true;
    }

    private void FreezeRoster(int[] playerCounts, int[] playerTeams)
    {
        int offset = 0;
        PlayerTeams = playerTeams.ToArray();
        PeerSlots = playerCounts
            .Select(n =>
            {
                var slots = Enumerable.Range(offset, n).ToArray();
                offset += n;
                return slots;
            })
            .ToArray();
        rosterFrozen = true;
    }

    private void SendControl(string address, Control control)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(control);
        byte[] data = new byte[json.Length + 5];
        BitConverter.TryWriteBytes(data, Magic);
        data[4] = 1;
        json.CopyTo(data, 5);
        wire.Send(address, data, true);
    }

    public IPeerTransport CreateTransport() => Ready ? this : throw new InvalidOperationException("Lobby is not ready");

    public void Send(int peer, ReadOnlySpan<byte> data)
    {
        if (!Ready || peer == LocalPeer || peer < 0 || peer >= PeerSlots.Length || data.Length > 1200)
        {
            return;
        }

        byte[] packet = new byte[data.Length + 23];
        BitConverter.TryWriteBytes(packet, Magic);
        packet[4] = 2;
        Guid.ParseExact(host == null ? nonce : acceptedNonce, "N").ToByteArray().CopyTo(packet, 5);
        packet[21] = (byte)LocalPeer;
        packet[22] = (byte)peer;
        data.CopyTo(packet.AsSpan(23));
        string target = host ?? addresses.Single(x => x.Value == peer).Key;
        wire.Send(target, packet, false);
    }

    public bool TryReceive(out Datagram datagram) => incoming.TryDequeue(out datagram);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        wire.Dispose();
    }

    public sealed class Control
    {
        public LobbyMessageKind Kind { get; set; }
        public string Hash { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string ClientNonce { get; set; } = "";
        public int Players { get; set; }
        public int Peer { get; set; }
        public int[] Counts { get; set; } = [];
        public int[] Teams { get; set; } = [];
        public string Settings { get; set; } = "";
    }
}
