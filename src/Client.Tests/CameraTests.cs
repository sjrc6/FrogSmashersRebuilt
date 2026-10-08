using FrogSmashers.Client;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;

internal static class CameraTests
{
    public static void Run(Action<bool, string> check)
    {
        var world = new World(
            new MapData(),
            new GameRules(playerCount: 4, format: MatchFormat.Teams, teams: [0, 0, 1, 1])
        );
        var camera = new GameCamera(new()) { ShakeEnabled = false };
        var first = world.Players[0];
        var teammate = world.Players[1];
        first.Alive = teammate.Alive = true;
        first.X = -25;
        first.Y = -10;
        teammate.X = 25;
        teammate.Y = 10;
        world.Players[2].Alive = true;
        world.Players[2].X = 45;
        world.Match.WinRound(world.Rules, 0);
        world.Match.PhaseTicks = 0;
        Settle(camera, world);
        foreach (var player in new[] { first, teammate })
        {
            var position = camera.ToScreen(new(player.X.ToFloat(), player.Y.ToFloat() + 1));
            check(
                position.X is > 20 and < 1260 && position.Y is > 20 and < 700,
                "Team celebration frames both distant winners"
            );
        }
        check(Math.Abs(camera.Position.X) < .1f, "The losing team does not pull the victory camera away");

        first.Alive = false;
        Settle(camera, world);
        var living = camera.ToScreen(new(teammate.X.ToFloat(), teammate.Y.ToFloat() + 1));
        check(
            living.X is > 0 and < 1280 && living.Y is > 0 and < 720,
            "A living teammate stays framed when the credited winner dies"
        );
        check(Math.Abs(camera.HalfHeight - 7.5f) < .01f, "A single living winner retains the original closeup");
        teammate.Alive = false;
        Settle(camera, world);
        check(
            camera.HalfHeight > 17.9f && camera.Position.Length() < .1f,
            "The camera returns to the arena when no winners are alive"
        );
        Console.WriteLine("Winning team camera framing checks passed");
    }

    private static void Settle(GameCamera camera, World world)
    {
        for (int frame = 0; frame < 600; frame++)
            camera.Update(world, 1 / 60f, frame / 60f);
    }
}
