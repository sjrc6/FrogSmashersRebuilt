using FrogSmashers.Core;

namespace FrogSmashers.Tests;

internal static class TestFixtures
{
    public static MapData Map() =>
        new()
        {
            Id = "test",
            Name = "Test arena",
            KillBounds = new BoundsData
            {
                Left = -35,
                Right = 35,
                Bottom = -24,
                Top = 60,
            },
            Collision =
            [
                new BoxData
                {
                    X = 0,
                    Y = -1,
                    Width = 42,
                    Height = 2,
                },
                new BoxData
                {
                    X = -8,
                    Y = 8,
                    Width = 12,
                    Height = 1,
                    OneWay = true,
                },
                new BoxData
                {
                    X = 17,
                    Y = 7,
                    Width = 2,
                    Height = 16,
                },
            ],
            Spawns = Enumerable.Range(0, 8).Select(p => new PointData { X = -14 + p * 4, Y = 2 }).ToList(),
            FlySpawn = new PointData { X = 0, Y = 12 },
        };

    public static World MakeWorld(int players) =>
        new(
            Map(),
            new GameRules
            {
                PlayerCount = players,
                WinScore = 99,
                MatchRounds = 3,
            },
            12345
        );

    public static InputFrame Input(long tick, int slot)
    {
        if (tick < 0)
        {
            return default;
        }

        uint seed = unchecked((uint)(tick / 18 + slot * 71 + 12345));
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        sbyte x = (sbyte)((int)(seed % 3) - 1);
        sbyte y = (sbyte)((int)((seed >> 4) % 3) - 1);
        InputButtons buttons = 0;
        if ((tick + slot * 13) % 59 < 16)
        {
            buttons |= InputButtons.Jump;
        }

        if ((tick + slot * 19) % 131 is > 40 and < 80)
        {
            buttons |= InputButtons.Attack;
        }

        if ((tick + slot * 7) % 157 is > 90 and < 98)
        {
            buttons |= InputButtons.Tongue;
        }

        if ((tick + slot * 11) % 103 is > 25 and < 55)
        {
            buttons |= InputButtons.Strafe;
        }

        return new(x, y, buttons);
    }
}
