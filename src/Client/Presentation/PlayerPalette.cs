using FrogSmashers.Core;
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

    public static Color Team(int team) =>
        team switch
        {
            0 => Color.Red,
            1 => Color.Blue,
            _ => Colors[team % Colors.Length],
        };

    public static Color For(World world, int slot) => Colors[world.Players[slot].ColorIndex];
}
