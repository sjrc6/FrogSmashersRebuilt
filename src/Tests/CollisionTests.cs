using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class CollisionTests
{
    public static void Run()
    {
        BodySweeps();
        Platforms();
        PartialPlatformDrops();
        TongueSweeps();
        Console.WriteLine("Swept collision, one-way terrain, friendly filtering and restoration passed");
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

    private static void PartialPlatformDrops()
    {
        foreach (bool fixes in new[] { false, true })
        foreach (bool lobby in new[] { false, true })
        {
            var map = new MapData
            {
                Id = "partial-drop",
                Spawns = [new() { X = 0, Y = 1 }, new() { X = 20, Y = 1 }],
                Collision =
                [
                    new()
                    {
                        Width = 12,
                        Height = 2,
                        OneWay = true,
                    },
                ],
            };
            var world = CreateWorld(new GameRules(lobby: lobby, modifiers: new() { PhysicsFixes = fixes }), map);
            if (lobby)
            {
                world.SetLobbySlot(0, true, 0);
                Step(world);
                world.BeachBall.Active = false;
            }
            var player = world.Players[0];
            player.Y = 1;
            player.VY = 0;
            Step(world, new(0, -1, InputButtons.Jump), 2);
            var embeddedY = player.Y;
            Check(embeddedY < 1 && embeddedY > -1, "A short drop enters the platform without clearing its bottom");
            var beforeRelease = world.Capture();
            Step(world, count: 20);
            Check(
                fixes ? !player.OnGround && player.Y < -1 : player.OnGround && player.Y == embeddedY,
                $"Physics Fixes controls partial platform embedding: fixes={fixes}, lobby={lobby}"
            );
            var released = world.Capture();
            world.Restore(beforeRelease);
            Step(world, count: 20);
            Check(
                world.Capture().SequenceEqual(released),
                "Releasing a partial drop survives rollback in both physics settings"
            );
            if (fixes)
                continue;

            var snapshot = world.Capture();
            Step(world, new(0, -1, InputButtons.Jump), 60);
            Check(
                world.Players[0].Y < -1 && !world.Players[0].OnGround,
                "Holding drop again exits an embedded platform"
            );
            var dropped = world.Capture();
            world.Restore(snapshot);
            Step(world, new(0, -1, InputButtons.Jump), 60);
            Check(world.Capture().SequenceEqual(dropped), "Partial platform drops survive rollback and replay");

            world.Restore(snapshot);
            Step(world, new(0, 0, InputButtons.Jump));
            Check(world.Players[0].VY > 0, "An embedded frog can jump back out of the platform");
        }
    }

    private static World TongueWorld(bool fixes, MapData map, int speed = 60, bool teams = false, bool lobby = false)
    {
        if (lobby)
            map.Spawns = TestFixtures.Map().Spawns;
        var world = new World(
            [map],
            new GameRules(
                playerCount: 3,
                lobby: lobby,
                format: teams ? MatchFormat.Teams : MatchFormat.Ffa,
                teams: [0, 0, 1],
                modifiers: new GameModifiers { PhysicsFixes = fixes }
            ),
            1,
            new() { ["tongueSpeed"] = speed, ["gravity"] = 0 }
        );
        foreach (var player in world.Players)
        {
            if (lobby)
                world.SetLobbySlot(player.Slot, true, player.Slot);
            player.Alive = player.Slot == 0;
            player.SpawnTicks = 10000;
            player.X = player.Y = 0;
        }
        world.Players[0].Mode = CharacterMode.Tongue;
        world.Players[0].TongueDistance = 4;
        world.BeachBall.X = 100;
        return world;
    }

    private static void TongueSweeps()
    {
        var thin = new MapData
        {
            Id = "thin",
            Collision =
            [
                new()
                {
                    X = 4.7m,
                    Y = 2,
                    Width = .001m,
                    Height = 8,
                },
            ],
        };
        var caught = TongueWorld(true, thin, 200);
        Step(caught);
        Check(
            caught.Players[0].TonguePhase == TonguePhase.AttachedToTerrain,
            "A high-speed tongue catches a thin wall"
        );
        var corner = new MapData
        {
            Id = "corner",
            Collision =
            [
                new()
                {
                    X = 3,
                    Y = 2.8m,
                    Width = 1,
                    Height = 1,
                },
                new()
                {
                    X = 9,
                    Y = 2,
                    Width = 1,
                    Height = 8,
                },
            ],
        };
        var around = TongueWorld(true, corner);
        var frog = around.Players[0];
        frog.TongueDistance = 5;
        frog.VY = 80;
        Step(around);
        Check(
            frog.TonguePhase == TonguePhase.Extending,
            "Moving the frog around a corner does not collide the already extended tongue shaft"
        );
        frog.VY = 0;
        for (int tick = 0; tick < 8 && frog.TonguePhase == TonguePhase.Extending; tick++)
            Step(around);
        Check(
            frog.TonguePhase == TonguePhase.AttachedToTerrain,
            "A tongue past a corner still latches onto a later wall"
        );
        foreach (bool fixes in new[] { false, true })
        foreach (bool lobby in new[] { false, true })
        {
            var world = TongueWorld(fixes, new() { Id = "friendly" }, teams: !lobby, lobby: lobby);
            world.Players[1].Alive = world.Players[2].Alive = true;
            world.Players[1].X = 5;
            world.Players[1].Mode = CharacterMode.Tongue;
            world.Players[1].TongueX = -1;
            world.Players[1].TongueDistance = 1;
            world.Players[1].TongueDelayLeft = 10;
            world.Players[2].X = 9;
            Step(world);
            Check(
                world.Players[0].TonguePhase == TonguePhase.Extending,
                "Tongues pass through friendly frogs and their tongues in both physics modes"
            );
            for (int tick = 0; tick < 8; tick++)
                Step(world);
            Check(
                lobby ? world.Players[2].LastHitBy == -1 : world.Players[2].LastHitBy == 0,
                "Lobby tongues pass all frogs; team tongues can still hit an opponent beyond a teammate"
            );
        }
        var snapshot = caught.Capture();
        Step(caught, count: 30);
        var expected = caught.Capture();
        caught.Restore(snapshot);
        Step(caught, count: 30);
        Check(caught.Capture().SequenceEqual(expected), "A caught thin-wall latch restores and resimulates exactly");
    }
}
