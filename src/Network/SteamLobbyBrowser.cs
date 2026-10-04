using System.Diagnostics;
using Steamworks;

namespace FrogSmashers.Network;

public sealed class SteamLobbyBrowser : ILobbyBrowser
{
    private readonly string fingerprint;
    private readonly int partySize;
    private readonly CallResult<LobbyMatchList_t> search;
    private readonly Callback<LobbyDataUpdate_t> metadata;
    private readonly Dictionary<ulong, LobbyListing> found = new();
    private readonly HashSet<ulong> candidates = new();
    private readonly HashSet<ulong> pendingFriends = new();
    private readonly Stopwatch clock = new();
    private bool pendingSearch;
    public IReadOnlyList<LobbyListing> Results { get; private set; } = [];
    public bool Searching => pendingSearch || pendingFriends.Count > 0;
    public string? Error { get; private set; }

    public SteamLobbyBrowser(string fingerprint, int partySize)
    {
        this.fingerprint = fingerprint;
        this.partySize = partySize;
        search = CallResult<LobbyMatchList_t>.Create(OnSearch);
        metadata = Callback<LobbyDataUpdate_t>.Create(OnMetadata);
        Refresh();
    }

    public void Refresh()
    {
        search.Cancel();
        found.Clear();
        candidates.Clear();
        pendingFriends.Clear();
        Results = [];
        Error = null;
        clock.Restart();
        SteamMatchmaking.AddRequestLobbyListStringFilter(
            "game",
            SteamLobby.GameTag,
            ELobbyComparison.k_ELobbyComparisonEqual
        );
        SteamMatchmaking.AddRequestLobbyListStringFilter(
            "protocol",
            NetworkBuild.Protocol,
            ELobbyComparison.k_ELobbyComparisonEqual
        );
        SteamMatchmaking.AddRequestLobbyListStringFilter(
            "compatibility",
            fingerprint,
            ELobbyComparison.k_ELobbyComparisonEqual
        );
        SteamMatchmaking.AddRequestLobbyListStringFilter("state", "forming", ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(100);
        pendingSearch = true;
        search.Set(SteamMatchmaking.RequestLobbyList());

        int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (int i = 0; i < count; i++)
        {
            var friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            if (
                !SteamFriends.GetFriendGamePlayed(friend, out var game)
                || game.m_gameID.AppID() != SteamUtils.GetAppID()
            )
                continue;
            ulong id = game.m_steamIDLobby.m_SteamID;
            if (id != 0 && candidates.Add(id) && SteamMatchmaking.RequestLobbyData(game.m_steamIDLobby))
                pendingFriends.Add(id);
        }
    }

    private void OnSearch(LobbyMatchList_t result, bool failed)
    {
        pendingSearch = false;
        if (failed)
        {
            Error = "STEAM SEARCH FAILED";
            return;
        }
        for (int i = 0; i < Math.Min(result.m_nLobbiesMatching, 100); i++)
        {
            var lobby = SteamMatchmaking.GetLobbyByIndex(i);
            candidates.Add(lobby.m_SteamID);
            UpdateListing(lobby);
        }
    }

    private void OnMetadata(LobbyDataUpdate_t update)
    {
        if (update.m_ulSteamIDMember != update.m_ulSteamIDLobby || !candidates.Contains(update.m_ulSteamIDLobby))
            return;
        pendingFriends.Remove(update.m_ulSteamIDLobby);
        if (update.m_bSuccess != 0)
            UpdateListing(new CSteamID(update.m_ulSteamIDLobby));
    }

    private void UpdateListing(CSteamID lobby)
    {
        found.Remove(lobby.m_SteamID);
        var owner = SteamMatchmaking.GetLobbyOwner(lobby);
        bool friend = SteamFriends.GetFriendRelationship(owner) == EFriendRelationship.k_EFriendRelationshipFriend;
        string Read(string key) => SteamMatchmaking.GetLobbyData(lobby, key);
        var listing = ReadListing(lobby.m_SteamID, Read, fingerprint, friend, partySize);
        if (
            listing != null
            && owner != SteamUser.GetSteamID()
            && SteamMatchmaking.GetNumLobbyMembers(lobby) < SteamMatchmaking.GetLobbyMemberLimit(lobby)
        )
            found[lobby.m_SteamID] = listing;
        Results = found.Values.OrderBy(item => item.Name).ThenBy(item => item.Id).ToArray();
    }

    internal static LobbyListing? ReadListing(
        ulong id,
        Func<string, string> read,
        string fingerprint,
        bool friend,
        int partySize
    )
    {
        if (
            read("game") != SteamLobby.GameTag
            || read("protocol") != NetworkBuild.Protocol
            || NetworkCompatibility.Rejection(fingerprint, read("compatibility")) != null
            || read("state") != "forming"
        )
            return null;
        if (
            !int.TryParse(read("capacity"), out int capacity)
            || capacity is < 1 or > 8
            || !int.TryParse(read("players"), out int players)
            || players < 0
            || players > capacity
            || !int.TryParse(read("open_slots"), out int open)
            || open < 0
            || !int.TryParse(read("friend_slots"), out int friends)
            || friends < 0
            || open > capacity - players
            || friends > capacity - players - open
        )
            return null;
        int available = open + (friend ? friends : 0);
        if (available < partySize)
            return null;
        return new(
            id.ToString(),
            "steam:" + id,
            LobbyListing.DisplayName(read("name"), "STEAM LOBBY"),
            players,
            capacity,
            available
        );
    }

    public void Poll()
    {
        if (Searching && clock.Elapsed.TotalSeconds > 20)
        {
            search.Cancel();
            pendingSearch = false;
            pendingFriends.Clear();
            Error = "STEAM SEARCH TIMED OUT";
        }
    }

    public void Dispose()
    {
        search.Dispose();
        metadata.Dispose();
    }
}
