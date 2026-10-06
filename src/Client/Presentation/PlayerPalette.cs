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
        if (world.Rules.Format != MatchFormat.Teams)
            return Colors[player.ColorIndex];
        int shade = 0;
        foreach (var other in world.Players)
            if (other.Team == player.Team)
            {
                if (other.ColorIndex < player.ColorIndex)
                    shade++;
            }
        return TeamShade(player.Team, shade);
    }

    public static Color Lobby(IReadOnlyList<LobbyPlayer?> rooms, int room, bool teams)
    {
        if (rooms[room] is not { } player)
            return Colors[room];
        if (!teams)
            return Colors[player.Color];
        int shade = 0;
        foreach (var other in rooms)
            if (other?.Team == player.Team)
            {
                if (other.Color < player.Color)
                    shade++;
            }
        return TeamShade(player.Team, shade);
    }

    private static Color TeamShade(int team, int shade) =>
        shade switch
        {
            1 => Color.Lerp(Team(team), Color.Black, .28f),
            2 => Color.Lerp(Team(team), Color.White, .25f),
            3 => Color.Lerp(Team(team), Color.Black, .5f),
            _ => Team(team),
        };
}
