using Steamworks;

namespace FrogSmashers.Network;

public sealed class SteamLobby : IGameLobby
{
    private readonly bool hosting;
    private readonly int peers;
    private readonly int localPlayers;
    private readonly int[]? localTeams;
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
    public ulong LobbyCode => lobbyId.m_SteamID;
    public ulong RequestedLobby { get; private set; }
    public bool Ready => relay?.Ready ?? false;
    public string Status => relay?.Status ?? status;
    public string? Error => error ?? relay?.Error;
    public int LocalPeer => relay?.LocalPeer ?? -1;
    public int[][] PeerSlots => relay?.PeerSlots ?? [];
    public int[] PlayerTeams => relay?.PlayerTeams ?? [];
    public string MatchSettingsJson => relay?.MatchSettingsJson ?? settings;

    public static SteamLobby Host(
        int peerCount,
        int localPlayers,
        string contentHash,
        string settingsJson,
        uint appId = 480,
        int[]? localTeams = null
    ) => new(true, 0, peerCount, localPlayers, contentHash, settingsJson, appId, localTeams);

    public static SteamLobby Join(
        ulong lobbyId,
        int localPlayers,
        string contentHash,
        uint appId = 480,
        int[]? localTeams = null
    ) => new(false, lobbyId, 0, localPlayers, contentHash, "", appId, localTeams);

    private SteamLobby(
        bool hosting,
        ulong joinId,
        int peers,
        int localPlayers,
        string hash,
        string settings,
        uint appId,
        int[]? localTeams
    )
    {
        if (localPlayers is < 1 or > 7 || hosting && (peers is < 2 or > 8 || localPlayers + peers - 1 > 8))
        {
            throw new ArgumentException("Invalid Steam lobby size");
        }

        this.localTeams = localTeams?.ToArray();
        if (
            this.localTeams != null
            && (this.localTeams.Length != localPlayers || this.localTeams.Any(t => t is < -1 or > 7))
        )
        {
            throw new ArgumentException("Invalid local team selections");
        }

        this.hosting = hosting;
        this.peers = peers;
        this.localPlayers = localPlayers;
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
                status = "Creating private Steam lobby";
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
                created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, peers));
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
        SteamMatchmaking.SetLobbyData(lobbyId, "protocol", "2");
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
            || SteamMatchmaking.GetLobbyData(lobbyId, "protocol") != "2"
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
            peers,
            localPlayers,
            contentHash,
            settings,
            localTeams
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
        if ((c.m_rgfChatMemberStateChange & leaving) != 0 && Ready)
        {
            error = "A Steam peer left the match";
        }
    }

    public void Poll()
    {
        if (disposed || !initialized || Error != null)
        {
            return;
        }

        SteamAPI.RunCallbacks();
        relay?.Poll();
        if (relay == null && startup.Elapsed.TotalSeconds > 120)
        {
            error = "Steam lobby setup timed out";
        }

        if (hosting && Ready && SteamMatchmaking.GetLobbyData(lobbyId, "state") != "started")
        {
            SteamMatchmaking.SetLobbyJoinable(lobbyId, false);
            SteamMatchmaking.SetLobbyData(lobbyId, "state", "started");
        }
    }

    public void InviteFriends()
    {
        if (initialized && lobbyId.m_SteamID != 0)
        {
            SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
        }
    }

    public IPeerTransport CreateTransport() =>
        Ready ? new SteamSessionTransport(this, relay!) : throw new InvalidOperationException("Lobby is not ready");

    private sealed class SteamSessionTransport(SteamLobby owner, RelayLobby relay) : IPeerTransport
    {
        public string? Error => owner.Error;

        public void Poll() => owner.Poll();

        public void Send(int peer, ReadOnlySpan<byte> data) => relay.Send(peer, data);

        public bool TryReceive(out Datagram datagram) => relay.TryReceive(out datagram);

        public void Dispose() => owner.Dispose();
    }

    public void Dispose()
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
