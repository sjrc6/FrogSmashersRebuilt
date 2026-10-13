using FrogSmashers.Core;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class ChargedTongueTests
{
    public static void Run()
    {
        LongTongueCollisions();
        var baseline = Shoot(0);
        foreach (int heldTicks in new[] { 1, 12, 25, 49, 50, 75, 150, 200 })
        {
            var shot = Shoot(heldTicks);
            Fixed multiplier = 1 + (Fixed)2 * Math.Min(heldTicks, World.TickRate / 2) / (World.TickRate / 2);
            Check(
                shot.Extension == baseline.Extension && shot.Retraction == baseline.Retraction,
                "Charge changes reach without changing extension or retraction time"
            );
            Check(
                Fixed.Abs(shot.Reach - baseline.Reach * multiplier) < Fixed.FromDecimal(.001m),
                "Charge grows smoothly from 1x to 3x over half a second and stays capped"
            );
        }

        var world = CreateWorld(new(modifiers: new() { ChargeTongues = true }));
        Step(world, new(0, 0, InputButtons.Tongue), 75);
        world.Players[1].X = 2;
        world.Players[1].Mode = CharacterMode.Attacking;
        world.Players[1].AttackPhase = AttackPhase.Swing;
        world.Players[1].AttackTimeLeft = 0;
        world.Players[1].AttackX = -1;
        Step(world, new(0, 0, InputButtons.Tongue));
        Check(
            world.Players[0].Mode == CharacterMode.Bouncing && world.Players[0].TongueChargeTicks == 0,
            "Getting hit interrupts and clears tongue charging"
        );
        Step(world);
        Check(
            !world.Events.Any(e => e.Kind == SimulationEventKind.TongueLaunch),
            "Releasing an interrupted charge does not fire a tongue"
        );

        foreach (int multiplier in new[] { 1, 3 })
        {
            var pull = CreateWorld(new(modifiers: new() { ChargeTongues = true, TongueJump = true }));
            var player = pull.Players[0];
            player.Mode = CharacterMode.Tongue;
            player.TonguePhase = TonguePhase.AttachedToTerrain;
            player.TongueLengthMultiplier = multiplier;
            player.TongueDistance = 10 * multiplier;
            player.Y = 6;
            player.OnGround = false;
            Fixed before = player.X;
            Step(pull);
            Check(
                Fixed.Abs(player.X - before - (Fixed)25 * multiplier * World.TickDuration) < Fixed.FromDecimal(.001m),
                "Charged terrain pulls scale speed along with tongue length"
            );
            Step(pull, new(0, 0, InputButtons.Jump));
            Check(player.Mode == CharacterMode.Normal && player.VY > 0, "Tongue Jump also cancels a charged pull");
        }
        Console.WriteLine(
            "Charged tongues: release, half-second charge cap, equal travel times and interruption passed"
        );
    }

    private static void LongTongueCollisions()
    {
        foreach (bool physicsFixes in new[] { false, true })
        foreach (string target in new[] { "player", "fly", "wall" })
        {
            var world = CreateWorld(new(modifiers: new() { ChargeTongues = true, PhysicsFixes = physicsFixes }));
            if (target == "wall")
            {
                world = CreateWorld(
                    world.Rules,
                    new MapData
                    {
                        Id = "charged-tongue-wall",
                        Spawns = [new() { X = 0 }, new() { X = 20 }],
                        Collision =
                        [
                            new()
                            {
                                X = 5,
                                Y = 2,
                                Width = 1,
                                Height = 4,
                            },
                        ],
                    }
                );
            }
            var player = world.Players[0];
            player.Mode = CharacterMode.Tongue;
            player.TonguePhase = TonguePhase.Extending;
            player.TongueLengthMultiplier = 3;
            player.TongueDistance = 3;
            if (target == "player")
                world.Players[1].X = 5;
            else if (target == "fly")
            {
                world.Fly.Active = true;
                world.Fly.X = 5;
                world.Fly.Y = player.TongueOrigin.Y;
            }

            Step(world);
            var expected = target switch
            {
                "player" => TonguePhase.RetractingHitEnemy,
                "fly" => TonguePhase.RetractingHitFly,
                _ => TonguePhase.AttachedToTerrain,
            };
            Check(player.TonguePhase == expected, "Long charged tongues cannot skip a " + target);
            Check(player.TongueTip.X <= 5, "The tongue stops at its first contact");
        }
    }

    private static (int Extension, int Retraction, Fixed Reach) Shoot(int heldTicks)
    {
        var world = CreateWorld(new(modifiers: new() { ChargeTongues = heldTicks > 0, FlyEnabled = false }));
        world.Players[1].Alive = false;
        world.Players[1].SpawnTicks = int.MaxValue;
        var held = new InputFrame(0, 0, InputButtons.Tongue);
        if (heldTicks > 0)
        {
            for (int tick = 0; tick < heldTicks; tick++)
                Step(world, held);
            Check(
                world.Players[0].TonguePhase == TonguePhase.Charging && world.Players[0].TongueDistance == 0,
                "Holding tongue charges without extending it"
            );
            Check(
                !world.Events.Any(e => e.Kind == SimulationEventKind.TongueLaunch),
                "Charging does not emit a launch"
            );
            byte[] charged = world.Capture();
            Step(world);
            byte[] released = world.Capture();
            world.Restore(charged);
            Step(world);
            Check(world.Capture().SequenceEqual(released), "Charge amount and release survive rollback");
        }
        else
            Step(world, held);

        Check(
            world.Events.Count(e => e.Kind == SimulationEventKind.TongueLaunch) == 1,
            "The tongue fires once on release"
        );
        int extension = 0;
        int retraction = 0;
        Fixed reach = 0;
        for (int tick = 0; tick < 300 && world.Players[0].Mode == CharacterMode.Tongue; tick++)
        {
            var player = world.Players[0];
            if (player.TonguePhase == TonguePhase.Extending)
                extension++;
            else
                retraction++;
            reach = Fixed.Max(reach, player.TongueDistance);
            Step(world);
        }
        Check(world.Players[0].Mode == CharacterMode.Normal, "The charged tongue finishes retracting");
        return (extension, retraction, reach);
    }
}
