using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class LobbyController
{
    public bool CanManage(LobbyPlayer player) =>
        Online?.Starting != true && (IsHost || !player.Cpu && player.Peer == LocalPeer);

    public bool CanRemove(LobbyPlayer player) => CanManage(player) && (IsHost || Roster.Humans(player.Peer).Length > 1);

    public string RemoveLabel(LobbyPlayer player) => player.Cpu || player.Peer != LocalPeer ? "KICK" : "BACK OUT";

    public void Remove(int peer, int id)
    {
        bool removed = Online != null ? Online.RemovePlayer(peer, id) : Roster.RemovePlayer(peer, id);
        if (!removed)
            game.Toasts.Show("CANNOT REMOVE PLAYER");
    }

    public void Spectate(int peer, bool spectating)
    {
        if (Online?.SetSpectating(peer, spectating) == true)
            return;
        game.Toasts.Show(
            spectating
                ? Roster.Spectators.Count >= LobbyRoster.MaxSpectators
                    ? "SPECTATORS FULL"
                    : "BACK OUT EXTRA PLAYERS FIRST"
                : "NO OPEN SLOTS"
        );
    }

    public int[] JoinHintDevices()
    {
        if (Roster.Spectator(LocalPeer) != null)
            return [];
        var devices = game.Controls.BindingDevices();
        int controllerCount = devices.Length - 2;
        int keyboards = Math.Clamp(8 - controllerCount, 0, 2);
        return devices
            .Where(device =>
                (device >= 2 || device < keyboards) && !LocalPlayers.Any(player => !player.Cpu && player.Id == device)
            )
            .Take(8)
            .ToArray();
    }
}
