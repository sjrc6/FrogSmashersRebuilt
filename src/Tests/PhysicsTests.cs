using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class PhysicsTests
{
    public static void FixedArithmetic()
    {
        Check(
            Fixed.Sqrt(FromDecimal(2)) * Fixed.Sqrt(FromDecimal(2)) > FromDecimal(1.99999m),
            "integer square root retains fixed precision"
        );
        Check(
            FromDecimal(200000) * FromDecimal(200000) == FromDecimal(40000000000m)
                && Fixed.Sqrt(FromDecimal(40000000000m)) == FromDecimal(200000),
            "wide fixed multiplication and square root retain precision beyond 64-bit intermediate range"
        );
        bool overflow = false;
        try
        {
            _ = -new Fixed(long.MinValue);
        }
        catch (OverflowException)
        {
            overflow = true;
        }

        Check(overflow, "fixed negation rejects the unrepresentable minimum-value magnitude");
    }

    public static void Movement()
    {
        var ground = CreateWorld();
        Step(ground, count: 240);
        Check(ground.Players[0].Y == Fixed.Zero && ground.Players[0].OnGround, "standing on a floor does not sink");
        Step(ground, new(1, 0, 0), 30);
        Check(ground.Players[0].VX == 20, "run speed caps at original 20 units/second");
        Step(ground, new(-1, 0, 0));
        Check(ground.Players[0].VX < 0, "ground turnaround reverses immediately");
        var momentum = CreateWorld();
        momentum.Players[0].OnGround = false;
        momentum.Players[0].Y = 10;
        momentum.Players[0].VX = 25;
        Step(momentum, new(-1, 0, 0));
        Check(
            momentum.Players[0].VX > 23 && momentum.Players[0].VX < 24,
            "opposite airborne input preserves and gradually brakes excess grapple momentum"
        );
        Step(momentum, new(1, 0, 0));
        Check(momentum.Players[0].VX == 20, "steering with excess momentum applies the original one-sided run cap");
        var footsteps = CreateWorld();
        int footstepCount = 0;
        for (int i = 0; i < 60; i++)
        {
            Step(footsteps, new(1, 0, 0));
            footstepCount += footsteps.Events.Count(e => e.Kind == SimulationEventKind.Footstep);
        }

        Check(footstepCount == 6, "run footsteps follow the original odd animation frames at .04 seconds per frame");
        footsteps.Players[0].HitstopTicks = 12;
        footsteps.Players[0].HitstopScale = 0;
        Step(footsteps, new(1, 0, 0), 12);
        Check(
            !footsteps.Events.Any(e => e.Kind == SimulationEventKind.Footstep),
            "hitstop pauses animation-derived footsteps"
        );
        var held = CreateWorld();
        var tap = CreateWorld();
        Fixed heldApex = 0;
        Fixed tapApex = 0;
        for (int tick = 0; tick < 150; tick++)
        {
            Step(held, new(0, 0, InputButtons.Jump));
            Step(tap, tick == 0 ? new(0, 0, InputButtons.Jump) : default);
            heldApex = Fixed.Max(heldApex, held.Players[0].Y);
            tapApex = Fixed.Max(tapApex, tap.Players[0].Y);
        }

        Check(heldApex > tapApex + 3, "holding jump preserves the original extended jump arc");
        var platform = new MapData
        {
            Id = "platform",
            Name = "platform",
            Collision =
            [
                new()
                {
                    X = 0,
                    Y = 0,
                    Width = 12,
                    Height = 1,
                    OneWay = true,
                },
            ],
            Spawns = [new() { X = 0, Y = .5m }],
        };
        var embed = CreateWorld(map: platform);
        embed.Players[0].Y = FromDecimal(.5m);
        Step(embed, new(0, -1, InputButtons.Jump), 2);
        var embeddedY = embed.Players[0].Y;
        Step(embed, count: 15);
        Check(
            embeddedY < FromDecimal(.5m) && embed.Players[0].Y == embeddedY && embed.Players[0].OnGround,
            "releasing drop-through inside a one-way platform preserves partial embedding"
        );
        Step(embed, new(0, -1, InputButtons.Jump), 20);
        Check(embed.Players[0].Y < -FromDecimal(.5m), "holding down+jump passes fully through one-way platforms");
        var safe = CreateWorld(new() { PreservePlatformEmbedding = false }, platform);
        safe.Players[0].Y = FromDecimal(.49m);
        Step(safe, count: 10);
        Check(!safe.Players[0].OnGround, "platform embedding compatibility flag is effective");
        var walls = CreateWorld(
            map: new()
            {
                Id = "wall",
                Name = "wall",
                Collision =
                [
                    new()
                    {
                        X = 10,
                        Y = 10,
                        Width = 2,
                        Height = 30,
                    },
                ],
                Spawns = [new() { X = 0, Y = 0 }],
            }
        );
        walls.Players[0].X = 8;
        walls.Players[0].Y = 8;
        walls.Players[0].OnGround = false;
        Step(walls, new(1, 0, 0), 10);
        Check(
            walls.Players[0].X == 8 && walls.Players[0].WallSliding,
            "solid wall clamps motion and enables wall slide"
        );
        Step(walls, new(1, 0, InputButtons.Jump));
        Check(walls.Players[0].VX < 0 && walls.Players[0].VY > 0, "wall jump launches away from the wall");
        Check(
            walls.Events.Any(e => e.Kind == SimulationEventKind.Jump && e.SurfaceSide == 1),
            "wall jump effects identify the contacted side"
        );
    }

    public static void Spawns()
    {
        var spawnMap = new MapData
        {
            Id = "spawn",
            Name = "spawn",
            Collision =
            [
                new()
                {
                    X = 0,
                    Y = -1,
                    Width = 100,
                    Height = 2,
                },
            ],
            Spawns = Enumerable.Range(0, 8).Select(i => new PointData { X = -35 + i * 10, Y = 0 }).ToList(),
        };
        var eight = new World(spawnMap, new() { PlayerCount = 8 }, 42);
        Step(eight, count: 240);
        Check(
            eight.Players.All(p => p.Alive && p.Facing == 1) && eight.Players.Select(p => p.X).Distinct().Count() == 8,
            "eight staggered spawns choose distinct safest points and preserve original default facing"
        );
    }

    public static void LongRunningClock()
    {
        var longRunning = CreateWorld();
        longRunning.Fly.Active = true;
        longRunning.Fly.Y = 5;
        longRunning.Fly.DirectionTicks = 1000;
        var future = longRunning.Capture();
        long futureTick = 120L * 60 * 60 * 24 * 365 * 100;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(future.AsSpan(16, 8), futureTick);
        longRunning.Restore(future);
        Step(longRunning);
        Check(
            longRunning.TickNumber == futureTick + 1
                && longRunning.Fly.Y > FromDecimal(4.9m)
                && longRunning.Fly.Y < FromDecimal(5.1m),
            "hundred-year tick clocks retain bounded fly motion without intermediate overflow"
        );
    }

    public static void ContentTuning()
    {
        var content = new GameContent
        {
            Maps = [TestFixtures.Map()],
            CharacterParameters = new Dictionary<string, decimal> { ["maxRunSpeed"] = 7, ["jumpVel"] = 13 },
        };
        var world = new World(content, new GameRules(), 123);
        foreach (var player in world.Players)
        {
            player.Alive = true;
            player.OnGround = true;
            player.X = player.Slot * 20;
        }
        Step(world, new InputFrame(1, 0, InputButtons.None), 30);
        Check(world.Players[0].VX == 7, "loaded character tuning controls the run speed cap");
        Step(world, new InputFrame(0, 0, InputButtons.Jump));
        Check(world.Players[0].VY == 13, "loaded character tuning controls jump velocity");
    }
}
