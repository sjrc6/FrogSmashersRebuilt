using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class BotNavigationTests
{
    public static void DownSmash(GameContent content)
    {
        foreach (int side in new[] { -1, 1 })
        foreach (int startX in new[] { 21, 26, 29 })
        {
            var world = new World(content, new() { MapOrder = [1], WinScore = 999 }, 123);
            var bot = world.Players[0];
            var target = world.Players[1];
            bot.Alive = target.Alive = true;
            bot.OnGround = target.OnGround = true;
            bot.X = side * startX;
            bot.Y = FromDecimal(side < 0 ? 7.98m : 7.99m);
            target.X = side * startX;
            target.Y = FromDecimal(-10.71m);

            Check(BotController.GetInput(world, 0).X == -side, $"DownSmash {side}: bot heads around the inner edge");
            bool reached = false;
            for (int tick = 0; tick < 600 && !reached; tick++)
            {
                Step(world, BotController.GetInput(world, 0));
                Check(bot.Alive, $"DownSmash {side}: descent stays within safe bounds");
                reached = bot.Y < -8 && Abs(bot.X - target.X) < 5;
            }

            Check(reached, $"DownSmash {side}: bot reaches an opponent below the overhang");
        }

        var center = new World(content, new() { MapOrder = [1], WinScore = 999 }, 123);
        var frog = center.Players[0];
        var opponent = center.Players[1];
        frog.Alive = opponent.Alive = true;
        frog.OnGround = opponent.OnGround = true;
        frog.X = 0;
        frog.Y = FromDecimal(-5.21m);
        opponent.X = 15;
        opponent.Y = FromDecimal(-15.69m);
        var input = BotController.GetInput(center, 0);
        Check(!(input.Jump && input.Y < 0), "DownSmash: bot does not drop through the center platform into the pit");
        bool landed = false;
        for (int tick = 0; tick < 480 && !landed; tick++)
        {
            Step(center, BotController.GetInput(center, 0));
            Check(frog.Alive, "DownSmash: bot survives the descent from the center platform");
            landed = frog.Y < -12 && frog.X > 10;
        }

        Check(landed, "DownSmash: bot leaves the center platform over a side floor");
    }

    public static void PlatformsAndCombat()
    {
        var world = CreateWorld();
        world.Map.Collision.Add(
            new()
            {
                X = 0,
                Y = 6,
                Width = 20,
                Height = .5m,
                OneWay = true,
            }
        );
        // Construct again so the collision map includes the thin platform.
        world = CreateWorld(map: world.Map);
        var bot = world.Players[0];
        var target = world.Players[1];
        bot.Y = FromDecimal(6.25m);
        target.X = 5;
        var input = BotController.GetInput(world, 0);
        Check(input.Jump && input.Y < 0, "bot drops through a thin platform with safe ground below");
        for (int tick = 0; tick < 120 && bot.Y > 1; tick++)
        {
            Step(world, BotController.GetInput(world, 0));
        }

        Check(bot.Alive && bot.Y < 1, "bot holds drop input until it clears the thin platform");

        var blocked = CreateWorld();
        blocked.Map.Collision.Add(
            new()
            {
                X = 0,
                Y = 5,
                Width = 12,
                Height = 4,
            }
        );
        blocked = CreateWorld(map: blocked.Map);
        blocked.Players[0].Y = 7;
        blocked.Players[1].X = 0;
        input = BotController.GetInput(blocked, 0);
        Check(
            input.X != 0 && !input.Attack && !input.Jump,
            "bot routes around a solid ledge instead of attacking or jumping"
        );

        var gap = CreateWorld(
            map: new()
            {
                Collision =
                [
                    new()
                    {
                        X = 0,
                        Y = -1,
                        Width = 8,
                        Height = 2,
                    },
                ],
            }
        );
        gap.Players[0].X = 3;
        gap.Players[1].X = 15;
        gap.Players[1].Y = -5;
        Check(BotController.GetInput(gap, 0).Jump, "bot still jumps over a gap without safe ground below");

        world = CreateWorld();
        world.Players[1].X = 4;
        Check(BotController.GetInput(world, 0).Attack, "bot still attacks a nearby unobstructed opponent");
    }
}
