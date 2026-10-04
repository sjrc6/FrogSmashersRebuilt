using FrogSmashers.Core;

namespace FrogSmashers.Tests;

internal static class MechanicsFixture
{
    public static World CreateWorld(GameRules? rules = null, MapData? map = null)
    {
        map ??= new MapData
        {
            Id = "test",
            Name = "test",
            Collision =
            [
                new()
                {
                    X = 0,
                    Y = -1,
                    Width = 80,
                    Height = 2,
                },
            ],
            Spawns = [new() { X = -5, Y = 0 }, new() { X = 5, Y = 0 }],
            FlySpawn = new() { X = 0, Y = 6 },
        };
        var world = new World(map, rules ?? new GameRules(), 123);
        foreach (var p in world.Players)
        {
            p.Alive = true;
            p.OnGround = true;
            p.X = p.Slot * 20;
            p.Y = 0;
        }

        return world;
    }

    public static void Step(World world, InputFrame p0 = default, int count = 1)
    {
        var input = new MatchInput[world.Players.Length];
        input[0] = new(p0);
        for (int i = 0; i < count; i++)
        {
            world.Advance(input);
        }
    }
}
