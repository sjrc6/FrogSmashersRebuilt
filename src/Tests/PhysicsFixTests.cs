using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class PhysicsFixTests
{
    public static void Run()
    {
        BodySweeps();
        Platforms();
        TongueSweeps();
        Console.WriteLine(
            "Physics fixes: thin terrain, body edges, wall slides, speed, earliest tongue hits and restoration passed"
        );
    }

    private static World Geometry(params BoxData[] boxes)
    {
        var world = CreateWorld(
            map: new()
            {
                Id = "geometry",
                Collision = boxes.ToList(),
                KillBounds = new()
                {
                    Left = -1000,
                    Right = 1000,
                    Bottom = -1000,
                    Top = 1000,
                },
            }
        );
        world.Players[1].Alive = false;
        world.Players[1].SpawnTicks = 10000;
        world.Players[0].OnGround = false;
        return world;
    }

    private static void BodySweeps()
    {
        foreach (decimal width in new[] { .001m, .2m, 1.5m })
        {
            var floor = Geometry(
                new BoxData()
                {
                    X = 0,
                    Y = 0,
                    Width = width,
                    Height = .1m,
                }
            );
            var p = floor.Players[0];
            p.Y = 5;
            p.VY = -700;
            p.Mode = CharacterMode.Bouncing;
            p.HasReachedApex = true;
            Step(floor);
            Check(
                p.OnGround && Fixed.Abs(p.Y - FromDecimal(.05m)) < FromDecimal(.001m),
                $"Swept full body lands on a {width}-unit ledge between the old foot rays at high speed"
            );
            var roof = Geometry(
                new BoxData()
                {
                    X = 0,
                    Y = 5,
                    Width = width,
                    Height = .1m,
                }
            );
            p = roof.Players[0];
            p.VY = 700;
            Step(roof);
            Check(
                p.VY == 0 && p.Y + 2 <= FromDecimal(4.951m),
                "Head sweep stops at a narrow ceiling between corner rays"
            );
        }
        foreach (int direction in new[] { -1, 1 })
        {
            var wall = Geometry(
                new BoxData()
                {
                    X = direction * 4,
                    Y = 1,
                    Width = .01m,
                    Height = .2m,
                }
            );
            var p = wall.Players[0];
            p.VX = direction * 700;
            Step(wall);
            Check(
                Fixed.Abs(p.X) <= FromDecimal(2.996m) && p.VX == 0,
                "A short wall between side rays blocks high-speed horizontal motion on either side"
            );
            p.X = direction * FromDecimal(2.995m);
            p.Y = -FromDecimal(.1m);
            p.VY = -3;
            Step(wall, new((sbyte)direction, 0, 0), 3);
            Check(
                p.WallSliding && p.WallSlideSide == direction,
                "Wall slide uses the body's current contact rather than a foot ray"
            );
            Step(wall, new((sbyte)direction, 0, InputButtons.Jump));
            Check(p.VX * direction < 0 && p.VY > 0, "Full-body wall contact supports wall jumps");
        }
        var diagonal = Geometry(
            new BoxData()
            {
                X = 5,
                Y = 5,
                Width = .2m,
                Height = .2m,
            }
        );
        diagonal.Players[0].VX = 800;
        diagonal.Players[0].VY = 800;
        Step(diagonal);
        var frog = diagonal.Players[0];
        Check(
            frog.X <= FromDecimal(3.901m) || frog.Y + 2 <= FromDecimal(4.901m),
            "A diagonal sweep cannot skip an isolated corner between the start and end positions"
        );
        var edge = Geometry(
            new BoxData()
            {
                X = 0,
                Y = -1,
                Width = 4,
                Height = 2,
            }
        );
        edge.Players[0].X = 3;
        edge.Players[0].VY = -2;
        Step(edge);
        Check(
            edge.Players[0].Y < 0 && !edge.Players[0].OnGround,
            "Zero-width edge contact does not create an invisible supporting ledge"
        );
        var floorRun = Geometry(
            new BoxData()
            {
                X = 0,
                Y = -1,
                Width = 10,
                Height = 2,
            }
        );
        floorRun.Players[0].VX = 20;
        Step(floorRun, count: 10);
        Check(
            floorRun.Players[0].X > 0 && floorRun.Players[0].Y >= -FromDecimal(.001m),
            "A floor contact permits tangential movement without sinking"
        );
    }

    private static void Platforms()
    {
        BoxData platform = new()
        {
            X = 0,
            Y = 0,
            Width = 12,
            Height = 1,
            OneWay = true,
        };
        var fall = Geometry(platform);
        fall.Players[0].Y = 5;
        fall.Players[0].Mode = CharacterMode.Bouncing;
        fall.Players[0].HasReachedApex = true;
        fall.Players[0].VY = -700;
        Step(fall);
        Check(
            fall.Players[0].OnGround && Fixed.Abs(fall.Players[0].Y - FromDecimal(.5m)) < FromDecimal(.001m),
            "Fast downward body sweeps land on one-way tops"
        );
        Step(fall, new(0, -1, InputButtons.Jump), 2);
        Step(fall, count: 15);
        Check(
            fall.Players[0].Y < FromDecimal(.5m) && !fall.Players[0].OnGround,
            "Releasing drop-through inside a platform continues the drop with fixes on"
        );
        var upward = Geometry(platform);
        upward.Players[0].Y = -3;
        upward.Players[0].VY = 700;
        Step(upward);
        Check(upward.Players[0].Y > 2, "Upward body sweeps pass through a one-way platform");
        var sideways = Geometry(platform);
        sideways.Players[0].X = -9;
        sideways.Players[0].Y = -1;
        sideways.Players[0].VX = 700;
        Step(sideways);
        Check(sideways.Players[0].X > -3, "A one-way platform never blocks side contact");
        var spike = Geometry(platform);
        spike.Players[0].Y = 5;
        spike.Players[0].VY = -700;
        spike.Players[0].Mode = CharacterMode.Bouncing;
        spike.Players[0].WasHitDownwards = true;
        Step(spike);
        Check(spike.Players[0].Y < 0, "Downward bat knockback still bypasses one-way platforms");
    }

    private static World Tongue(decimal wallX, decimal width = .001m, bool oneWay = false)
    {
        var world = Geometry(
            new BoxData()
            {
                X = wallX,
                Y = 0,
                Width = width,
                Height = 20,
                OneWay = oneWay,
            }
        );
        var p = world.Players[0];
        p.Mode = CharacterMode.Tongue;
        p.TonguePhase = TonguePhase.Extending;
        p.WasBouncingBeforeTongue = true;
        p.TongueDistance = 4;
        return world;
    }

    private static void TongueSweeps()
    {
        var ordering = new World(
            [new() { Id = "ordered-hits" }],
            new(playerCount: 3),
            1,
            new() { ["tongueSpeed"] = 1200, ["gravity"] = 0 }
        );
        foreach (var player in ordering.Players)
            player.Alive = true;
        ordering.Players[0].Mode = CharacterMode.Tongue;
        ordering.Players[0].TongueDistance = 4;
        ordering.Players[1].X = 9;
        ordering.Players[2].X = 6;
        ordering.Fly.Active = true;
        ordering.Fly.X = 10;
        ordering.Fly.Y = FromDecimal(1.5m);
        ordering.Fly.DirectionTicks = 100;
        Step(ordering);
        Check(
            ordering.Players[2].LastHitBy == 0 && ordering.Players[1].LastHitBy == -1 && ordering.Fly.Owner == -1,
            "Earliest contact wins over player iteration order and the old fly-first priority"
        );
        foreach (int speed in new[] { -800, -20, 0, 20, 800 })
        {
            var world = Tongue(6);
            var p = world.Players[0];
            p.VX = speed;
            bool stopped = false;
            for (int i = 0; i < 10; i++)
            {
                Step(world);
                if (p.TonguePhase != TonguePhase.Extending)
                {
                    stopped = true;
                    Check(p.TongueTip.X <= FromDecimal(5.501m), "Swept tongue remains on the near side of a thin wall");
                    break;
                }
            }
            Check(stopped, $"Tongue stops at thin wall with initial frog velocity {speed}");
        }
        var near = Tongue(2);
        near.Players[0].TongueDistance = 0;
        Step(near, count: 3);
        Check(
            near.Players[0].TonguePhase == TonguePhase.Retracting && near.Players[0].TongueTip.X < 2,
            "A wall inside minimum grapple range stops and retracts the tongue instead of letting it pass"
        );

        var blockedFly = Tongue(5);
        blockedFly.Fly.Active = true;
        blockedFly.Fly.X = 7;
        blockedFly.Fly.Y = FromDecimal(1.5m);
        blockedFly.Fly.DirectionTicks = 100;
        blockedFly.Players[0].VX = 300;
        Step(blockedFly);
        Check(
            blockedFly.Fly.Owner == -1 && blockedFly.Players[0].TonguePhase == TonguePhase.Retracting,
            "Terrain wins before a fly behind it, even when the tip crosses both in one tick"
        );
        var visibleFly = Tongue(8);
        visibleFly.Fly.Active = true;
        visibleFly.Fly.X = 6;
        visibleFly.Fly.Y = FromDecimal(1.5m);
        visibleFly.Fly.DirectionTicks = 100;
        visibleFly.Players[0].VX = 300;
        Step(visibleFly);
        Check(visibleFly.Fly.Owner == 0, "A fly encountered before the wall can be caught");

        var enemies = Tongue(20);
        enemies.Players[1].Alive = true;
        enemies.Players[1].X = 7;
        enemies.Players[1].Y = 0;
        enemies.Players[0].VX = 300;
        Step(enemies);
        Check(
            enemies.Players[1].LastHitBy == 0 && enemies.Players[0].TonguePhase == TonguePhase.RetractingHitEnemy,
            "Swept tongue hits an enemy crossed between endpoints"
        );
        var blockedEnemy = Tongue(5);
        blockedEnemy.Players[1].Alive = true;
        blockedEnemy.Players[1].X = 8;
        blockedEnemy.Players[1].Y = 0;
        blockedEnemy.Players[0].VX = 300;
        Step(blockedEnemy);
        Check(blockedEnemy.Players[1].LastHitBy == -1, "A frog behind the first wall cannot be tongued");

        foreach (int direction in new[] { -1, 1 })
        {
            var world = Geometry(
                new BoxData()
                {
                    X = 0,
                    Y = 0,
                    Width = 10,
                    Height = .1m,
                    OneWay = true,
                }
            );
            var p = world.Players[0];
            p.Y = direction < 0 ? 6 : -8;
            p.Mode = CharacterMode.Tongue;
            p.TongueY = direction;
            p.TongueX = 0;
            p.WasBouncingBeforeTongue = true;
            p.TongueDistance = 4;
            bool latched = false;
            for (int tick = 0; tick < 8; tick++)
            {
                Step(world);
                latched |= world.Events.Any(e => e.Kind == SimulationEventKind.TongueLatch);
            }
            Check(latched == (direction < 0), "Tongue sweeps respect the top-only direction of one-way platforms");
        }
        var snapshot = Tongue(6);
        var saved = snapshot.Capture();
        Step(snapshot, count: 30);
        var expected = snapshot.Capture();
        snapshot.Restore(saved);
        Step(snapshot, count: 30);
        Check(
            snapshot.Capture().SequenceEqual(expected),
            "Restoring before a swept latch reproduces the complete grapple"
        );
        var edge = Geometry(
            new BoxData
            {
                X = 0,
                Y = 0,
                Width = 2,
                Height = .1m,
                OneWay = true,
            }
        );
        var edgeShot = edge.Players[0];
        edgeShot.X = FromDecimal(1.25m);
        edgeShot.Y = 4;
        edgeShot.Mode = CharacterMode.Tongue;
        edgeShot.TongueX = 0;
        edgeShot.TongueY = -1;
        edgeShot.WasBouncingBeforeTongue = true;
        edgeShot.TongueDistance = 4;
        bool edgeLatch = false;
        for (int tick = 0; tick < 5; tick++)
        {
            Step(edge);
            edgeLatch |= edge.Events.Any(e => e.Kind == SimulationEventKind.TongueLatch);
        }
        Check(edgeLatch, "The tongue's circular tip can catch the edge of a one-way platform from above");
    }
}
