using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class PlayerPalette
{
    public static readonly Color[] Colors =
    [
        new(170, 176, 88),
        new(133, 104, 132),
        new(242, 158, 158),
        new(206, 9, 19),
        new(89, 89, 89),
        new(183, 187, 177),
        new(255, 181, 77),
        new(64, 178, 216),
    ];

    public static Color Team(int team) => Colors[team];

    public static Color For(World world, int slot)
    {
        var player = world.Players[slot];
        if (!world.Rules.UsesTeams)
            return Colors[player.ColorIndex];
        int count = 0,
            shade = 0;
        foreach (var other in world.Players)
            if (other.Team == player.Team)
            {
                count++;
                if (other.ColorIndex < player.ColorIndex)
                    shade++;
            }
        return TeamShade(player.Team, shade, count);
    }

    public static Color Lobby(IReadOnlyList<LobbyPlayer?> rooms, int room, bool teams)
    {
        if (rooms[room] is not { } player)
            return Colors[room];
        if (!teams)
            return Colors[player.Color];
        int count = 0,
            shade = 0;
        foreach (var other in rooms)
            if (other?.Team == player.Team)
            {
                count++;
                if (other.Color < player.Color)
                    shade++;
            }
        return TeamShade(player.Team, shade, count);
    }

    private static Color TeamShade(int team, int shade, int count) =>
        Color.Lerp(Team(team), Color.White, count <= 1 ? 0 : .7f * shade / (count - 1));
}
