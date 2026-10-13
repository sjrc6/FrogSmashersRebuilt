using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class TongueFlyModifierTests
{
    public static void Run()
    {
        TongueHurtbox();
        TongueJump();
        ChargedTongueTests.Run();
        FlyBatting();
        FlyRecovery();
        FlyCapture();
        FlyExplosion();
        Console.WriteLine("Tongue hurtboxes, pull jumps, fly tumble/recovery and radial explosions passed");
    }

    private static void Swing(PlayerState player, int x = 1, int y = 0)
    {
        player.Mode = CharacterMode.Attacking;
        player.AttackPhase = AttackPhase.Swing;
        player.AttackTimeLeft = 0;
        player.AttackX = x;
        player.AttackY = y;
    }

    private static void ReplayStep(World world, InputFrame input = default, int count = 1)
    {
        byte[] before = world.Capture();
        Step(world, input, count);
        byte[] after = world.Capture();
        var events = world.Events.ToArray();
        world.Restore(before);
        Step(world, input, count);
        Check(world.Capture().SequenceEqual(after), "Modifier simulation survives snapshot restore and resimulation");
        Check(world.Events.SequenceEqual(events), "Modifier presentation events replay deterministically");
    }

    private static void TongueHurtbox()
    {
        foreach (bool enabled in new[] { false, true })
        foreach (var phase in Enum.GetValues<TonguePhase>())
        {
            var world = CreateWorld(new(modifiers: new() { TongueHurtbox = enabled }));
            Swing(world.Players[0], 0, 1);
            var target = world.Players[1];
            target.X = 10;
            target.Mode = CharacterMode.Tongue;
            target.TonguePhase = phase;
            target.TongueX = -1;
            target.TongueDistance = 20;
            target.TongueDelayLeft = 1;
            ReplayStep(world);
            target = world.Players[1];
            bool hit = enabled && phase is not (TonguePhase.Stunned or TonguePhase.Burping or TonguePhase.Charging);
            Check(target.HitsTaken == (hit ? 1 : 0), "The exposed tongue shaft uses its owner's bat hurtbox");
            if (hit)
                Check(
                    target.Mode == CharacterMode.Bouncing && target.HitstopTicks > 0 && target.LastHitBy == 0,
                    "Batting a tongue applies ordinary hit stun, tumble and attacker credit"
                );
        }

        var overlap = CreateWorld(new(modifiers: new() { TongueHurtbox = true }));
        Swing(overlap.Players[0]);
        overlap.Players[1].X = 2;
        overlap.Players[1].Mode = CharacterMode.Tongue;
        overlap.Players[1].TongueDistance = 3;
        ReplayStep(overlap);
        Check(overlap.Players[1].HitsTaken == 1, "A bat overlapping both body and tongue hits the owner once");

        var team = CreateWorld(
            new(playerCount: 3, format: MatchFormat.Teams, teams: [0, 0, 1], modifiers: new() { TongueHurtbox = true })
        );
        Swing(team.Players[0], 0, 1);
        team.Players[1].X = 10;
        team.Players[1].Mode = CharacterMode.Tongue;
        team.Players[1].TongueX = -1;
        team.Players[1].TongueDistance = 20;
        ReplayStep(team);
        Check(team.Players[1].HitsTaken == 0, "Tongue hurtboxes preserve friendly-fire rules");
    }

    private static World Pulling(bool enabled, bool bounced = false)
    {
        var world = CreateWorld(new(modifiers: new() { TongueJump = enabled }));
        var player = world.Players[0];
        player.Y = 6;
        player.OnGround = false;
        player.Mode = CharacterMode.Tongue;
        player.TonguePhase = TonguePhase.AttachedToTerrain;
        player.TongueX = 1;
        player.TongueY = 0;
        player.TongueDistance = 8;
        player.WasBouncingBeforeTongue = bounced;
        player.HitsTaken = bounced ? 3 : 0;
        return world;
    }

    private static void TongueJump()
    {
        var jump = new InputFrame(0, 0, InputButtons.Jump);
        foreach (bool bounced in new[] { false, true })
        foreach (decimal distance in new[] { .1m, 5m, 9m })
        {
            var world = Pulling(true, bounced);
            world.Players[0].TongueDistance = FromDecimal(distance);
            ReplayStep(world, jump);
            var player = world.Players[0];
            Check(
                player.Mode == CharacterMode.Normal && player.TongueDistance == 0 && player.VY > 0,
                "A fresh jump cancels a terrain pull at any distance"
            );
            Check(!player.WasBouncingBeforeTongue && player.HitsTaken == 0, "Jumping out also ends a bounce tongue");
            Fixed upward = player.VY;
            Step(world, jump);
            Check(world.Players[0].VY < upward, "Holding jump does not repeatedly renew the tongue jump");
        }
        var disabled = Pulling(false);
        ReplayStep(disabled, jump);
        Check(
            disabled.Players[0].Mode == CharacterMode.Tongue,
            "Terrain pulls retain their original behavior by default"
        );
        var held = Pulling(true);
        held.Players[0].PreviousInput = jump;
        ReplayStep(held, jump);
        Check(held.Players[0].Mode == CharacterMode.Tongue, "Already-held jump does not cancel a new pull");
        var extending = Pulling(true);
        extending.Players[0].TonguePhase = TonguePhase.Extending;
        ReplayStep(extending, jump);
        Check(extending.Players[0].Mode == CharacterMode.Tongue, "Tongue jump requires an attached terrain pull");
    }

    private static World WithFly(GameModifiers modifiers)
    {
        var world = CreateWorld(new(modifiers: modifiers));
        world.Fly.Active = true;
        world.Fly.X = 2;
        world.Fly.Y = 1;
        world.Fly.DirectionTicks = 1000;
        return world;
    }

    private static void FlyBatting()
    {
        foreach (bool enabled in new[] { false, true })
        {
            var world = WithFly(new() { FlyTumble = enabled });
            Swing(world.Players[0]);
            ReplayStep(world);
            Check(
                world.Fly.Phase == (enabled ? FlyPhase.Tumbling : FlyPhase.Flying),
                "Only fly tumble enables bat launches"
            );
            if (!enabled)
                continue;
            Check(
                world.Fly.HitsTaken == 1 && world.Fly.HitstopTicks > 0,
                "A batted fly receives hitstop and combo power"
            );
            Check(
                world.Fly.VX > 0 && world.Fly.VY > 0 && !world.Fly.HasBounced,
                "Fly launches follow the bat direction"
            );
            var position = world.Fly.Position;
            ReplayStep(world, count: world.Fly.HitstopTicks);
            Check(world.Fly.Position == position, "The fly stays frozen throughout its hitstop");
            ReplayStep(world);
            Check(world.Fly.X > position.X, "The fly moves after hitstop finishes");
        }
    }

    private static void FlyRecovery()
    {
        foreach (bool bounced in new[] { false, true })
        {
            var world = WithFly(new() { FlyTumble = true });
            world.Fly.Phase = FlyPhase.Tumbling;
            world.Fly.HasBounced = bounced;
            world.Fly.Y = 6;
            world.Fly.VY = FromDecimal(.1m);
            ReplayStep(world);
            Check(world.Fly.Phase == FlyPhase.Flying, "Apex recovery is immediate and does not require a bounce");
        }
        foreach (bool bounced in new[] { false, true })
        {
            var world = WithFly(new() { FlyTumble = true });
            world.Fly.Phase = FlyPhase.Tumbling;
            world.Fly.HasBounced = bounced;
            world.Fly.Y = FlyState.Radius + FromDecimal(.005m);
            world.Fly.VY = -1;
            ReplayStep(world);
            Check(
                world.Fly.Phase == (bounced ? FlyPhase.Recovering : FlyPhase.Tumbling),
                "Low-speed landing requires an earlier bounce before surface recovery"
            );
            if (!bounced)
            {
                Check(world.Fly.HasBounced && world.Fly.VY > 0, "The first low-speed landing still bounces");
                continue;
            }
            var position = world.Fly.Position;
            Check(world.Fly.VX == 0 && world.Fly.VY == 0, "Surface recovery stops the fly without skidding");
            ReplayStep(world, count: world.Fly.RecoveryTicks - 1);
            Check(
                world.Fly.Position == position && world.Fly.Phase == FlyPhase.Recovering,
                "The fly sticks during recovery"
            );
            ReplayStep(world);
            Check(world.Fly.Phase == FlyPhase.Flying, "Surface recovery returns the fly to ordinary flight");
        }
        var fast = WithFly(new() { FlyTumble = true });
        fast.Fly.Phase = FlyPhase.Tumbling;
        fast.Fly.HasBounced = true;
        fast.Fly.Y = FlyState.Radius + FromDecimal(.005m);
        fast.Fly.VX = 10;
        fast.Fly.VY = -30;
        ReplayStep(fast);
        Check(
            fast.Fly.Phase == FlyPhase.Tumbling && fast.Fly.VY > 0 && fast.Fly.VX == 10,
            "Fast ground impacts bounce without applying skid friction"
        );
    }

    private static void FlyCapture()
    {
        var world = WithFly(new() { FlyTumble = true });
        world.Fly.Phase = FlyPhase.Tumbling;
        world.Fly.HitsTaken = 3;
        world.Fly.HitstopTicks = 5;
        world.Fly.Y = FromDecimal(1.5m);
        var player = world.Players[0];
        player.Mode = CharacterMode.Tongue;
        player.TonguePhase = TonguePhase.Extending;
        player.TongueDistance = 1;
        ReplayStep(world);
        Check(
            world.Fly.Owner == 0 && world.Players[0].TonguePhase == TonguePhase.RetractingHitFly,
            "A tumbling fly remains catchable by a tongue"
        );
        Check(
            world.Fly.Phase == FlyPhase.Flying && world.Fly.HitstopTicks == 0 && world.Fly.HitsTaken == 0,
            "Catching a fly clears its tumble state"
        );
    }

    private static void FlyExplosion()
    {
        var world = CreateWorld(
            new(playerCount: 4, format: MatchFormat.Teams, teams: [0, 0, 1, 1], modifiers: new() { FlyExplode = true })
        );
        FixedVector[] positions = [new(0, 0), new(2, 0), new(25, 25), new(-25, 25)];
        for (int slot = 0; slot < world.Players.Length; slot++)
        {
            world.Players[slot].X = positions[slot].X;
            world.Players[slot].Y = positions[slot].Y;
        }
        world.Fly.Active = true;
        world.Fly.X = 2;
        world.Fly.Y = 1;
        Swing(world.Players[0]);
        ReplayStep(world);
        Check(world.Fly.Active && world.Fly.Phase == FlyPhase.Exploding, "The fly freezes before its explosion");
        Check(
            world.Events.Single(e => e.Kind == SimulationEventKind.FlyHit).HitstopSeconds == FromDecimal(.5m),
            "Explosion hit effects last half a second"
        );
        var origin = world.Fly.Position;
        ReplayStep(world, count: world.Fly.HitstopTicks);
        Check(
            world.Fly.Active && world.Fly.Position == origin && world.Fly.HitstopTicks == 0,
            "The exploding fly stays in place throughout its half-second hitstop"
        );
        ReplayStep(world);
        Check(!world.Fly.Active && world.Fly.SpawnTicks > 0, "An exploded fly disappears and schedules its next spawn");
        Check(
            world.Players.All(p => p.Mode == CharacterMode.Bouncing && p.HitsTaken == 1 && p.HitstopTicks > 0),
            "Fly explosions hit every living player, including the batter and their teammate"
        );
        Check(
            world.Players[0].VX < 0 && world.Players[0].VY == 0,
            "Explosion knockback points radially away without bat lift"
        );
        Check(world.Players[1].VX == 0 && world.Players[1].VY > 0, "A player at the explosion center launches upward");
        Check(
            world.Players[2].VX > 0 && world.Players[2].VY > 0 && world.Players[3].VX < 0 && world.Players[3].VY > 0,
            "Distant players launch away from the same explosion center"
        );
        Check(
            world.Events.Count(e => e.Kind == SimulationEventKind.FlyExplode) == 1
                && world.Events.Count(e => e.Kind == SimulationEventKind.Hit && e.HitKind == HitKind.FlyExplosion) == 4,
            "An explosion emits one blast and one hit per player"
        );
    }
}
