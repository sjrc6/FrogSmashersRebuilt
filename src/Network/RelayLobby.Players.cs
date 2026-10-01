namespace FrogSmashers.Network;

internal sealed partial class RelayLobby
{
    public bool SetPlayers(LobbyPlayer[] players)
    {
        if (!Connected || Starting)
            return false;
        if (IsHost)
        {
            if (!Roster.SetPlayers(0, players))
                return false;
            Changed();
            return true;
        }
        var validation = CopyRoster();
        if (!ApplyParty(validation, LocalPeer, players, false, localAccess))
            return false;
        requested = players.ToArray();
        requestedSpectating = false;
        RequestChanged();
        return true;
    }

    public bool EditSlot(int room, SlotType type)
    {
        if (!IsHost || Starting || !Roster.Edit(room, type))
            return false;
        Changed();
        return true;
    }

    public bool ApplySlotType(SlotType type)
    {
        if (!IsHost || Starting || !Enum.IsDefined(type))
            return false;
        Roster.ApplySlotType(type);
        Changed();
        return true;
    }

    public bool RemovePlayer(int peer, int id)
    {
        if (!Connected || Starting || !IsHost && peer != LocalPeer)
            return false;
        var player = Roster.Players(peer).FirstOrDefault(player => player.Id == id);
        if (player == null)
            return false;
        if (!IsHost)
            return SetPlayers(Roster.Players(peer).Where(player => player.Id != id).ToArray());
        if (peer > 0 && Roster.Humans(peer).Length == 1)
        {
            Kick(peer, false);
            return true;
        }
        Roster.RemovePlayer(peer, id);
        if (peer > 0)
            epochs[peer]++;
        Changed();
        return true;
    }

    public bool SetSpectating(int peer, bool spectating)
    {
        if (!Connected || Starting || !IsHost && peer != LocalPeer)
            return false;
        var validation = CopyRoster();
        if (!validation.SetSpectating(peer, spectating, IsHost ? AccessFor(peer) : localAccess))
            return false;
        if (IsHost)
        {
            Roster.Replace(validation.Slots, validation.Spectators);
            if (peer > 0)
                epochs[peer]++;
            Changed();
        }
        else
        {
            requested = spectating ? [validation.Spectator(peer)!] : validation.Players(peer);
            requestedSpectating = spectating;
            RequestChanged();
        }
        return true;
    }

    private LobbyRoster CopyRoster()
    {
        var copy = new LobbyRoster();
        copy.Replace(Roster.Slots, Roster.Spectators);
        return copy;
    }

    private static bool ApplyParty(
        LobbyRoster roster,
        int peer,
        LobbyPlayer[] players,
        bool spectating,
        LobbyAccess access
    )
    {
        if (players.Length == 0 || players.Any(player => player.Cpu))
            return false;
        if (spectating || roster.Spectator(peer) != null)
        {
            var humans = roster.Humans(peer);
            var saved = roster.Spectator(peer) ?? (humans.Length == 1 ? humans[0] : null);
            if (players.Length != 1 || saved == null || players[0].Id != saved.Id)
                return false;
            if (!roster.SetSpectating(peer, spectating, access))
                return false;
            if (spectating)
                return true;
        }
        return roster.SetPlayers(peer, players, access);
    }

    private LobbyAccess AccessFor(int peer)
    {
        if (peer == 0)
            return default;
        string? address = addresses.FirstOrDefault(pair => pair.Value == peer).Key;
        return access.GetValueOrDefault(peer) with { Friend = address != null && isFriend(address) };
    }

    private void RequestChanged()
    {
        requestVersion++;
        lastSend = -1000;
    }
}
