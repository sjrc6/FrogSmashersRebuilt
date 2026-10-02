using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private (int Peer, int Id)? selectedPlayer;

    private LobbyPlayer[] ListedPlayers() =>
        [
            .. game.Lobby.Roster.Slots.Where(slot => slot.Player != null).Select(slot => slot.Player!),
            .. game.Lobby.Roster.Spectators,
        ];

    private string PlayerLabel(LobbyPlayer player)
    {
        if (!player.Cpu && player.Peer == game.Lobby.LocalPeer)
            return new LocalSeat(player.Id).Label;
        int room = game.Lobby.Roster.Slots.ToList().FindIndex(slot => slot.Player == player);
        return player.Cpu ? $"CPU {room + 1}"
            : room >= 0 ? $"PLAYER {room + 1}"
            : $"GUEST {player.Peer}";
    }

    private string? PlayerActionDisabledReason(LobbyPlayer player) =>
        ShowingMatch || game.Lobby.Online?.Starting == true ? "LOBBY ONLY"
        : game.Lobby.RosterUpdating ? "LOBBY UPDATING"
        : !game.Lobby.CanManage(player) ? "HOST ONLY"
        : null;

    private IReadOnlyList<MenuEntry> PlayerRows()
    {
        var rows = new List<MenuEntry>();
        bool firstSpectator = true;
        foreach (var player in ListedPlayers())
        {
            bool spectator = game.Lobby.Roster.Spectator(player.Peer) == player;
            rows.Add(
                new(
                    PlayerLabel(player),
                    Value: spectator ? "SPECTATOR" : null,
                    DisabledReason: PlayerActionDisabledReason(player),
                    SeparatorBefore: spectator && firstSpectator
                )
            );
            if (spectator)
                firstSpectator = false;
        }
        rows.Add(new("BACK", Back));
        return rows;
    }

    public (MenuEntry? Accept, MenuEntry? Remove) PlayerActions()
    {
        var player = ListedPlayers().ElementAtOrDefault(Selected);
        if (player == null)
            return (null, null);
        string? disabled = PlayerActionDisabledReason(player);
        MenuEntry Action(string label, Action activate) => new(label, activate, DisabledReason: disabled);
        if (player.Cpu)
            return (null, Action("KICK", () => game.Lobby.Remove(player.Peer, player.Id)));

        bool spectator = game.Lobby.Roster.Spectator(player.Peer) == player;
        bool multiple = game.Lobby.Roster.Humans(player.Peer).Length > 1;
        bool remote = player.Peer != game.Lobby.LocalPeer;
        MenuEntry accept =
            spectator ? Action("UNSPECTATE", () => game.Lobby.Spectate(player.Peer, false))
            : game.Lobby.Online == null || multiple
                ? Action("BACK OUT", () => game.Lobby.Remove(player.Peer, player.Id))
            : Action("SPECTATE", () => game.Lobby.Spectate(player.Peer, true));
        MenuEntry? remove = null;
        if (remote && game.Lobby.IsHost)
            remove = Action("KICK", () => game.Online.Lobby!.Kick(player.Peer, false));
        else if (!spectator && !multiple && game.Lobby.Online != null && game.Lobby.IsHost)
            remove = Action("BACK OUT", () => game.Lobby.Remove(player.Peer, player.Id));
        return (accept, remove);
    }

    private void UpdatePlayerList(MenuInput input)
    {
        if (input.Back)
        {
            Back();
            return;
        }
        FollowSelectedPlayer();
        var entries = PlayerRows();
        SelectRow(Wrap(Selected + input.Vertical, entries.Count));
        var layout = MeasurePointer(entries);
        bool clickedRow = ClickRow(entries, layout);
        var player = ListedPlayers().ElementAtOrDefault(Selected);
        selectedPlayer = player == null ? null : (player.Peer, player.Id);
        if (clickedRow)
            return;

        var actions = PlayerActions();
        if (input.Accept && actions.Accept != null)
            ActivateEntry(actions.Accept);
        else if (input.Accept && Selected == entries.Count - 1)
            Back();
        else if (input.Remove && actions.Remove != null)
            ActivateEntry(actions.Remove);
        else if (game.Controls.MousePressed && Pointer() is { } point)
        {
            var panel = layout!.Panel;
            bool paired = actions.Accept != null && actions.Remove != null;
            if (MenuLayout.PlayerBack(panel, paired).Contains(point))
                Back();
            else if (actions.Accept != null && MenuLayout.PlayerAction(panel, false, paired).Contains(point))
                ActivateEntry(actions.Accept);
            else if (actions.Remove != null && MenuLayout.PlayerAction(panel, true, paired).Contains(point))
                ActivateEntry(actions.Remove);
        }
        if (Screen == GameScreen.ViewPlayers)
            FollowSelectedPlayer();
    }

    private void FollowSelectedPlayer()
    {
        var players = ListedPlayers();
        int index = selectedPlayer is { } identity
            ? Array.FindIndex(players, player => player.Peer == identity.Peer && player.Id == identity.Id)
            : -1;
        Selected = index >= 0 ? index : Math.Min(Selected, players.Length);
    }

    private void CreateOnlineLobby()
    {
        Creation.Apply(game.Setup.Lobby.Roster);
        AllowLan = Creation.Lan;
        game.BeginLobby(Creation.Lan ? "udp" : "steam", true);
    }
}
