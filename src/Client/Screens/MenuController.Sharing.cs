using FrogSmashers.Network;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private readonly Dictionary<ulong, (int Image, Texture2D Texture)> friendAvatars = new();
    private double avatarRefresh;

    public Texture2D? FriendAvatar(ulong id) => friendAvatars.GetValueOrDefault(id).Texture;

    private void UpdateFriendAvatars(double elapsed)
    {
        avatarRefresh -= elapsed;
        if (avatarRefresh > 0 || game.Online.Lobby is not SteamLobby lobby)
            return;
        avatarRefresh = .5;
        foreach (var friend in friends)
        {
            var previous = friendAvatars.GetValueOrDefault(friend.Id);
            if (lobby.FriendAvatar(friend.Id, previous.Image) is not { } avatar)
                continue;
            var texture = new Texture2D(game.GraphicsDevice, avatar.Width, avatar.Height);
            texture.SetData(avatar.Pixels);
            previous.Texture?.Dispose();
            friendAvatars[friend.Id] = (avatar.Image, texture);
        }
    }

    private void ClearFriendAvatars()
    {
        foreach (var avatar in friendAvatars.Values)
            avatar.Texture.Dispose();
        friendAvatars.Clear();
    }

    public void Dispose() => ClearFriendAvatars();

    private SteamLobby? pendingShareLobby;
    private bool copyAfterCreation;
    private IReadOnlyList<SteamFriend> friends = [];
    private readonly HashSet<ulong> invitedFriends = new();

    private void StartPrivateLobby(bool copy)
    {
        Creation.SelectPrivate();
        Creation.Reset(game.Lobby.Roster);
        CreateOnlineLobby();
        pendingShareLobby = game.Online.Lobby as SteamLobby;
        copyAfterCreation = copy;
    }

    private void CompleteLobbyShare()
    {
        var pending = pendingShareLobby;
        pendingShareLobby = null;
        if (pending == null || !ReferenceEquals(pending, game.Online.Lobby))
            return;
        if (copyAfterCreation)
            CopyLobbyCode();
        else
            InviteFriends();
    }

    private void CopyLobbyCode()
    {
        if (game.Online.Lobby == null)
        {
            StartPrivateLobby(copy: true);
            return;
        }
        if (game.Online.Lobby is not SteamLobby lobby)
            return;
        string? code = lobby.ShareCode;
        if (code == null)
        {
            game.Toasts.Show(lobby.ShareCodeUnavailable ?? "CODE UNAVAILABLE");
            return;
        }
        try
        {
            game.Toasts.Show(Clipboard.WriteText(code) ? "LOBBY CODE COPIED" : "CANNOT WRITE CLIPBOARD");
        }
        catch (Exception error)
            when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            game.Toasts.Show("CANNOT WRITE CLIPBOARD");
        }
    }

    private void InviteFriends()
    {
        if (game.Online.Lobby == null)
        {
            StartPrivateLobby(copy: false);
            return;
        }
        if (game.Online.Lobby is not SteamLobby { IsHost: true } lobby)
            return;
        ClearFriendAvatars();
        avatarRefresh = 0;
        friends = lobby.Friends();
        invitedFriends.Clear();
        Open(GameScreen.InviteFriends);
    }

    private IReadOnlyList<MenuEntry> FriendRows()
    {
        var lobby = game.Online.Lobby as SteamLobby;
        string? disabled = lobby is { Connected: true, IsHost: true, Starting: false } ? null : "LOBBY NOT READY";
        var rows = friends
            .Select(friend => new MenuEntry(
                "friend-" + friend.Id,
                friend.Name,
                () => SendInvitation(friend.Id),
                Role: MenuRole.Positive,
                Avatar: friend.Id,
                Value: invitedFriends.Contains(friend.Id) ? "INVITED"
                    : friend.Online ? "ONLINE"
                    : "OFFLINE",
                ValueSample: "INVITED",
                DisabledReason: disabled
            ))
            .ToList();
        if (rows.Count == 0)
            rows.Add(new("no-friends", "NO FRIENDS FOUND", DisabledReason: "NO FRIENDS FOUND"));
        rows.Add(new("refresh", "REFRESH", () => friends = lobby?.Friends() ?? []));
        rows.Add(BackRow());
        return rows;
    }

    private void SendInvitation(ulong friend)
    {
        bool sent = game.Online.Lobby is SteamLobby lobby && lobby.InviteFriend(friend);
        if (sent)
            invitedFriends.Add(friend);
        game.Toasts.Show(sent ? "INVITATION SENT" : "INVITATION FAILED");
    }

    private void ChangeLobbyCode()
    {
        if (game.Online.Lobby is not SteamLobby { IsHost: true, Privacy: LobbyPrivacy.PrivateCode } lobby)
            return;
        lobby.RotateAdmissionSecret();
        invitedFriends.Clear();
        game.Toasts.Show("CODE CHANGED - COPY TO SHARE AGAIN");
    }
}
