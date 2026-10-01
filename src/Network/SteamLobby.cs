using Steamworks;

namespace FrogSmashers.Network;

public sealed class SteamLobby : GameLobby
{
    private readonly bool hosting;
    private readonly int capacity;
    private readonly LobbyPlayer[] players;
    private readonly LobbySlot[]? initialRooms;
    private readonly string contentHash;
    private readonly string settings;
    private readonly List<IDisposable> callbacks = new();
    private readonly System.Diagnostics.Stopwatch startup = System.Diagnostics.Stopwatch.StartNew();
    private RelayLobby? relay;
    private SteamWire? wire;
    private CSteamID lobbyId;
    private bool initialized;
    private bool disposed;
    private string status = "Initializing Steam";
    private string? error;
    private string advertisedCapacity = "";
    public ulong LobbyCode => lobbyId.m_SteamID;
    public ulong RequestedLobby { get; private set; }
    protected override IGameLobby? Active => relay;
    public override bool IsHost => hosting;
    public override string Status => relay?.Status ?? status;
    public override string? Error => error ?? relay?.Error;

    public static SteamLobby Host(
        int capacity,
        LobbyPlayer[] players,
        string contentHash,
        string settingsJson,
        uint appId = 480,
        IReadOnlyList<LobbySlot>? initialRooms = null
    ) => new(true, 0, capacity, players, contentHash, settingsJson, appId, initialRooms);

    public static SteamLobby Join(ulong lobbyId, LobbyPlayer[] players, string contentHash, uint appId = 480) =>
        new(false, lobbyId, 8, players, contentHash, "", appId);

    private SteamLobby(
        bool hosting,
        ulong joinId,
        int capacity,
        LobbyPlayer[] players,
        string hash,
        string settings,
        uint appId,
        IReadOnlyList<LobbySlot>? initialRooms = null
    )
    {
        var validation = new LobbyRoster();
        if (!validation.SetPlayers(0, players) || !validation.SetCapacity(capacity))
            throw new ArgumentException("Invalid Steam lobby configuration");
        if (hosting && initialRooms == null)
        {
            validation.ConfigureEmpty(SlotType.Private, capacity);
            initialRooms = validation.Slots;
        }
        this.hosting = hosting;
        this.capacity = capacity;
        this.players = players.ToArray();
        this.initialRooms = initialRooms?.ToArray();
        contentHash = hash;
        this.settings = settings;
        try
        {
            if (!SteamRuntime.TryAcquire(appId, out error))
            {
                return;
            }

            initialized = true;
            callbacks.Add(Callback<LobbyChatUpdate_t>.Create(OnMembershipChanged));
            callbacks.Add(Callback<GameLobbyJoinRequested_t>.Create(c => RequestedLobby = c.m_steamIDLobby.m_SteamID));
            if (hosting)
            {
                status = "Creating Steam lobby";
                var created = CallResult<LobbyCreated_t>.Create(
                    (result, failed) =>
                    {
                        if (failed)
                        {
                            error = "Steam lobby creation failed: API transport failure";
                        }
                        else
                        {
                            OnCreated(result);
                        }
                    }
                );
                callbacks.Add(created);
                created.Set(
                    SteamMatchmaking.CreateLobby(Visibility(initialRooms ?? validation.Slots), LobbyRoster.MaxPeers)
                );
            }
            else
            {
                status = "Joining Steam lobby";
                lobbyId = new CSteamID(joinId);
                var entered = CallResult<LobbyEnter_t>.Create(
                    (result, failed) =>
                    {
                        if (failed)
                        {
                            error = "Steam lobby entry failed: API transport failure";
                        }
                        else
                        {
                            OnEntered(result);
                        }
                    }
                );
                callbacks.Add(entered);
                entered.Set(SteamMatchmaking.JoinLobby(lobbyId));
            }
        }
        catch (DllNotFoundException ex)
        {
            error = $"Steam runtime unavailable: {ex.Message}";
        }
        catch (EntryPointNotFoundException ex)
        {
            error = $"Steam runtime version mismatch: {ex.Message}";
        }
        catch (BadImageFormatException ex)
        {
            error = $"Steam runtime architecture mismatch: {ex.Message}";
        }
    }

    private void OnCreated(LobbyCreated_t c)
    {
        if (!hosting)
        {
            return;
        }

        if (c.m_eResult != EResult.k_EResultOK)
        {
            error = $"Steam lobby creation failed: {c.m_eResult}";
            return;
        }

        lobbyId = new CSteamID(c.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(lobbyId, "game", "FrogSmashersRebuilt");
        SteamMatchmaking.SetLobbyData(lobbyId, "protocol", "6");
        SteamMatchmaking.SetLobbyData(lobbyId, "content", contentHash);
        SteamMatchmaking.SetLobbyData(lobbyId, "state", "forming");
        StartRelay();
    }

    private void OnEntered(LobbyEnter_t c)
    {
        if (c.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            error = $"Steam lobby entry failed: {(EChatRoomEnterResponse)c.m_EChatRoomEnterResponse}";
            return;
        }

        if (hosting)
        {
            return; // LobbyCreated publishes metadata before creating the listener.
        }

        lobbyId = new CSteamID(c.m_ulSteamIDLobby);
        if (
            SteamMatchmaking.GetLobbyData(lobbyId, "game") != "FrogSmashersRebuilt"
            || SteamMatchmaking.GetLobbyData(lobbyId, "protocol") != "6"
            || SteamMatchmaking.GetLobbyData(lobbyId, "content") != contentHash
        )
        {
            error = "Steam lobby belongs to another game/build/content version";
            return;
        }

        if (SteamMatchmaking.GetLobbyData(lobbyId, "state") != "forming")
        {
            error = "This match has already started";
            return;
        }

        StartRelay();
    }

    private void StartRelay()
    {
        if (relay != null)
        {
            return;
        }

        var owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
        wire = new SteamWire(lobbyId, hosting, owner);
        relay = new RelayLobby(
            wire,
            hosting ? null : owner.m_SteamID.ToString(),
            capacity,
            players,
            contentHash,
            settings,
            initialRooms,
            invited: !hosting,
            isFriend: address =>
                ulong.TryParse(address, out var id)
                && SteamFriends.GetFriendRelationship(new CSteamID(id))
                    == EFriendRelationship.k_EFriendRelationshipFriend
        );
    }

    private void OnMembershipChanged(LobbyChatUpdate_t c)
    {
        if (c.m_ulSteamIDLobby != lobbyId.m_SteamID || relay == null)
        {
            return;
        }

        if (SteamMatchmaking.GetLobbyOwner(lobbyId) != wire!.Owner)
        {
            error = "Lobby host disconnected; host migration is not supported";
            return;
        }

        const uint leaving = (uint)(
            EChatMemberStateChange.k_EChatMemberStateChangeLeft
            | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
            | EChatMemberStateChange.k_EChatMemberStateChangeKicked
            | EChatMemberStateChange.k_EChatMemberStateChangeBanned
        );
        if ((c.m_rgfChatMemberStateChange & leaving) != 0 && hosting)
            relay.RemovePeer(c.m_ulSteamIDUserChanged.ToString());
    }

    public override void Poll()
    {
        if (disposed || !initialized || Error != null)
        {
            return;
        }

        SteamAPI.RunCallbacks();
        if (relay != null && wire != null)
            while (wire.TakeDisconnected(out string address))
                relay.RemovePeer(address);
        relay?.Poll();
        if (relay == null && startup.Elapsed.TotalSeconds > 120)
        {
            error = "Steam lobby setup timed out";
        }

        if (hosting && relay != null)
        {
            int capacity = Roster.Capacity;
            int occupied = Roster.Count;
            int open = Roster.Slots.Count(slot => slot.Open && slot.Type == SlotType.Open && slot.Player == null);
            int friends = Roster.Slots.Count(slot => slot.Type == SlotType.Friend && slot.Player == null);
            int privateSlots = Roster.Slots.Count(slot => slot.Type == SlotType.Private && slot.Player == null);
            var visibility = Visibility(Roster.Slots);
            string advertisement =
                $"{capacity}:{occupied}:{open}:{friends}:{privateSlots}:{visibility}:{Roster.Spectators.Count}";
            if (advertisement != advertisedCapacity)
            {
                SteamMatchmaking.SetLobbyData(lobbyId, "capacity", capacity.ToString());
                SteamMatchmaking.SetLobbyData(lobbyId, "players", occupied.ToString());
                SteamMatchmaking.SetLobbyData(lobbyId, "open_slots", open.ToString());
                SteamMatchmaking.SetLobbyData(lobbyId, "friend_slots", friends.ToString());
                SteamMatchmaking.SetLobbyData(lobbyId, "private_slots", privateSlots.ToString());
                SteamMatchmaking.SetLobbyData(lobbyId, "spectators", Roster.Spectators.Count.ToString());
                SteamMatchmaking.SetLobbyType(lobbyId, visibility);
                advertisedCapacity = advertisement;
            }
        }

        string state = Starting ? "started" : "forming";
        if (hosting && relay != null && SteamMatchmaking.GetLobbyData(lobbyId, "state") != state)
        {
            SteamMatchmaking.SetLobbyJoinable(lobbyId, !Starting);
            SteamMatchmaking.SetLobbyData(lobbyId, "state", state);
        }
    }

    internal static ELobbyType Visibility(IEnumerable<LobbySlot> slots)
    {
        var types = slots.Where(slot => slot.Player?.Peer != 0).Select(slot => slot.Type).ToArray();
        if (types.Contains(SlotType.Open))
            return ELobbyType.k_ELobbyTypePublic;
        if (types.Contains(SlotType.Friend))
            return ELobbyType.k_ELobbyTypeFriendsOnly;
        return ELobbyType.k_ELobbyTypePrivate;
    }

    public void InviteFriends()
    {
        if (initialized && lobbyId.m_SteamID != 0)
        {
            SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
        }
    }

    public override IPeerTransport CreateTransport() =>
        Ready
            ? new SteamSessionTransport(this, relay!.CreateTransport())
            : throw new InvalidOperationException("Lobby is not ready");

    private sealed class SteamSessionTransport(SteamLobby owner, IPeerTransport session) : IPeerTransport
    {
        public string? Error => owner.Error;

        public void Poll() => owner.Poll();

        public void Send(int peer, ReadOnlySpan<byte> data) => session.Send(peer, data);

        public bool TryReceive(out Datagram datagram) => session.TryReceive(out datagram);

        public void Dispose() => session.Dispose();
    }

    public override void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        relay?.Dispose();
        wire?.Dispose();
        foreach (var callback in callbacks)
        {
            callback.Dispose();
        }

        if (initialized)
        {
            if (lobbyId.m_SteamID != 0)
            {
                SteamMatchmaking.LeaveLobby(lobbyId);
            }

            SteamRuntime.Release();
            initialized = false;
        }
    }
}
