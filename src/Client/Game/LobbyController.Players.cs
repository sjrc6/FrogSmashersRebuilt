using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class LobbyController
{
    public bool CanManage(LobbyPlayer player) =>
        Online?.Starting != true && (IsHost || !player.Cpu && player.Peer == LocalPeer);

    public bool CanKick(LobbyPlayer player) => CanManage(player) && (player.Cpu || player.Peer != LocalPeer);

    public bool BackOutSpectates(LobbyPlayer player) =>
        Online != null
        && !player.Cpu
        && (!IsHost || player.Peer != LocalPeer)
        && Roster.Humans(player.Peer).Length == 1;

    public string BackOutLabel(LobbyPlayer player) => BackOutSpectates(player) ? "SPECTATE" : "BACK OUT";

    public void BackOut(LobbyPlayer player)
    {
        if (!CanManage(player) || player.Cpu)
            return;
        if (BackOutSpectates(player))
            Spectate(player.Peer, true);
        else
            Remove(player.Peer, player.Id);
    }

    public void Kick(LobbyPlayer player)
    {
        if (!CanKick(player))
            return;
        if (player.Cpu)
            Remove(player.Peer, player.Id);
        else
            Online!.Kick(player.Peer, false);
    }

    public void Remove(int peer, int id)
    {
        bool removed =
            Online is { IsHost: false } && peer == LocalPeer
                ? Online.SetPlayers(Online.PendingLocalPlayers.Where(player => player.Id != id).ToArray())
            : Online != null ? Online.RemovePlayer(peer, id)
            : EditLocal(roster => roster.RemovePlayer(peer, id));
        if (!removed)
            game.Toasts.Show(RosterUpdating ? "LOBBY UPDATING" : "CANNOT REMOVE PLAYER");
        else if (peer == LocalPeer)
            commands.Remove(id);
        UpdatePresentation();
    }

    public void Spectate(int peer, bool spectating)
    {
        if (Online?.SetSpectating(peer, spectating) == true)
        {
            UpdatePresentation();
            if (!spectating && peer == LocalPeer)
            {
                var player = Online.PendingLocalPlayers.FirstOrDefault(player => !player.Cpu);
                if (player != null)
                    JoinFeedback(player.Id);
            }
            return;
        }
        game.Toasts.Show(
            RosterUpdating ? "LOBBY UPDATING"
            : spectating
                ? Roster.Spectators.Count >= LobbyRoster.MaxSpectators ? "SPECTATORS FULL"
                    : "CANNOT SPECTATE"
            : "NO OPEN SLOTS"
        );
    }

    public int[] JoinHintDevices()
    {
        var devices = game.Controls.BindingDevices();
        int controllerCount = devices.Length - 2;
        int keyboards = Math.Clamp(8 - controllerCount, 0, 2);
        return devices
            .Where(device =>
                (device >= 2 || device < keyboards)
                && !Presentation.Roster.Humans(LocalPeer).Any(player => player.Id == device)
            )
            .Take(8)
            .ToArray();
    }
}
