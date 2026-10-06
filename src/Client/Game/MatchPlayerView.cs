using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed record MatchPlayerView(string Name, int Device = -1);

internal static class LobbyPlayerLabel
{
    public static string Name(LobbyPlayer player, int room, int localPeer) =>
        player.Cpu ? "CPU"
        : player.Peer == localPeer ? new LocalSeat(player.Id).Label
        : $"PLAYER {room + 1}";
}
