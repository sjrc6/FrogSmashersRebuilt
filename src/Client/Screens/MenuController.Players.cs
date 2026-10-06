using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private LobbyPlayer[] ListedPlayers() =>
        [
            .. game.Lobby.Roster.Slots.Where(slot => slot.Player != null).Select(slot => slot.Player!),
            .. game.Lobby.Roster.Spectators,
        ];

    private int[] UnseatedPeers() =>
        game.Lobby.Online?.PeerIds.Where(peer =>
                game.Lobby.Roster.Humans(peer).Length == 0 && game.Lobby.Roster.Spectator(peer) == null
            )
            .ToArray()
        ?? [];

    private string PlayerLabel(LobbyPlayer player)
    {
        int room = game.Lobby.Roster.Slots.ToList().FindIndex(slot => slot.Player == player);
        return player.Cpu
            ? $"CPU {room + 1}"
            : LobbyPlayerLabel.Name(player, room, game.Lobby.LocalPeer, game.Lobby.Online);
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
                    $"player-{player.Peer}-{player.Id}",
                    PlayerLabel(player),
                    Value: spectator ? "SPECTATOR" : null,
                    DisabledReason: PlayerActionDisabledReason(player),
                    SeparatorBefore: spectator && firstSpectator
                )
            );
            if (spectator)
                firstSpectator = false;
        }
        foreach (int peer in UnseatedPeers())
            rows.Add(
                new($"connection-{peer}", LobbyPlayerLabel.PeerName(peer, game.Lobby.Online), Value: "NOT JOINED")
            );
        return rows;
    }

    public (MenuEntry? Accept, MenuEntry? Remove) PlayerActions()
    {
        var player = ListedPlayers().ElementAtOrDefault(Selected);
        if (player == null)
        {
            int index = Selected - ListedPlayers().Length;
            int[] peers = UnseatedPeers();
            if (index >= 0 && index < peers.Length && peers[index] != game.Lobby.LocalPeer && game.Lobby.IsHost)
            {
                int peer = peers[index];
                return (
                    null,
                    new(
                        "kick-connection",
                        "KICK",
                        () => game.Online.Lobby!.Kick(peer, false),
                        Role: MenuRole.Destructive,
                        DisabledReason: ShowingMatch ? "LOBBY ONLY" : null
                    )
                );
            }
            return (null, null);
        }
        string? disabled = PlayerActionDisabledReason(player);
        MenuEntry Action(string label, Action activate) =>
            new(
                "player-action-" + label,
                label,
                activate,
                DisabledReason: disabled,
                Role: label == "UNSPECTATE" ? MenuRole.Positive : MenuRole.Destructive
            );
        if (player.Cpu)
            return (null, Action("KICK", () => game.Lobby.Remove(player.Peer, player.Id)));

        bool spectator = game.Lobby.Roster.Spectator(player.Peer) == player;
        MenuEntry accept = spectator
            ? Action("UNSPECTATE", () => game.Lobby.Spectate(player.Peer, false))
            : Action(game.Lobby.BackOutLabel(player), () => game.Lobby.BackOut(player));
        MenuEntry remove = Action("KICK", () => game.Lobby.Kick(player)) with
        {
            Disabled = !game.Lobby.CanKick(player),
        };
        return (accept, remove);
    }

    private void UpdatePlayerList(MenuInput input)
    {
        if (input.Back)
        {
            Back();
            return;
        }
        var entries = Entries();
        if (entries.Count > 0)
            SelectRow(Wrap(Selected + input.Vertical, entries.Count));
        var layout = MeasurePointer(entries);
        bool clickedRow = ClickRow(entries, layout);
        if (clickedRow)
            return;

        var actions = PlayerActions();
        if (input.Accept && actions.Accept != null)
            ActivateEntry(actions.Accept);
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
    }
}
