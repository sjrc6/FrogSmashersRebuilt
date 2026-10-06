using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed record MatchPlayerView(string Name, int Device = -1);

internal static class LobbyPlayerLabel
{
    public static string Name(LobbyPlayer player, int room, int localPeer, IGameLobby? lobby = null)
    {
        if (player.Cpu)
            return "CPU";
        if (lobby is SteamLobby steam && steam.PeerName(player.Peer) is { } name)
        {
            var party = lobby.Roster.Humans(player.Peer).OrderBy(member => member.Id).ToArray();
            int index = Array.FindIndex(party, member => member.Id == player.Id);
            return index <= 0 ? name : name[..Math.Min(name.Length, 21)] + $"({index + 1})";
        }
        if (player.Peer == localPeer)
            return new LocalSeat(player.Id).Label;
        return room >= 0 ? $"PLAYER {room + 1}" : $"GUEST {player.Peer}";
    }

    public static string PeerName(int peer, IGameLobby? lobby) =>
        (lobby as SteamLobby)?.PeerName(peer) ?? (peer == 0 ? "HOST" : $"GUEST {peer}");
}
