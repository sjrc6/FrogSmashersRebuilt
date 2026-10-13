using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class TongueFlyModifierTests
{
    public static void Run()
    {
        TongueParry();
        TongueJump();
        ChargedTongueTests.Run();
        FlyBatting();
        FlyRecovery();
        FlyCapture();
        FlyExplosion();
        Console.WriteLine("Tongue parries, pull jumps, fly tumble/recovery and radial explosions passed");
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

    private static void TongueParry()
    {
        foreach (bool enabled in new[] { false, true })
        foreach (var phase in Enum.GetValues<TonguePhase>())
        {
            var world = CreateWorld(new(modifiers: new() { TongueParry = enabled }));
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

        var frozen = CreateWorld(new(modifiers: new() { TongueParry = true }));
        Swing(frozen.Players[0], 0, 1);
        var parried = frozen.Players[1];
        parried.X = 10;
        parried.Mode = CharacterMode.Tongue;
        parried.TongueX = -1;
        parried.TongueDistance = 20;
        var tip = parried.TongueTip;
        ReplayStep(frozen);
        Check(frozen.Players[1].TongueParried, "A tongue parry retains the extended tongue during hitstop");
        while (frozen.Players[1].HitstopTicks > 1)
        {
            Check(
                frozen.Players[1].TongueTip == tip && frozen.Players[1].TongueParried,
                "The parried tongue stays extended and stationary"
            );
            Step(frozen);
        }
        Step(frozen);
        Check(!frozen.Players[1].TongueParried, "The tongue disappears when parry hitstop ends");

        var overlap = CreateWorld(new(modifiers: new() { TongueParry = true }));
        Swing(overlap.Players[0]);
        overlap.Players[1].X = 2;
        overlap.Players[1].Mode = CharacterMode.Tongue;
        overlap.Players[1].TongueDistance = 3;
        ReplayStep(overlap);
        Check(overlap.Players[1].HitsTaken == 1, "A bat overlapping both body and tongue hits the owner once");

        var team = CreateWorld(
            new(playerCount: 3, format: MatchFormat.Teams, teams: [0, 0, 1], modifiers: new() { TongueParry = true })
        );
        Swing(team.Players[0], 0, 1);
        team.Players[1].X = 10;
        team.Players[1].Mode = CharacterMode.Tongue;
        team.Players[1].TongueX = -1;
        team.Players[1].TongueDistance = 20;
        ReplayStep(team);
        Check(team.Players[1].HitsTaken == 0, "Tongue parries preserve friendly-fire rules");
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
        foreach (decimal distance in new[] { .1m, 5m, 9m })
        {
            var world = Pulling(true);
            world.Players[0].TongueDistance = FromDecimal(distance);
            ReplayStep(world, jump);
            var player = world.Players[0];
            Check(
                player.Mode == CharacterMode.Normal && player.TongueDistance == 0 && player.VY > 0,
                "A fresh jump cancels a terrain pull at any distance"
            );
            var normal = CreateWorld();
            Step(normal);
            normal.Players[0].Y = 6;
            Step(normal, jump);
            for (int tick = 0; tick < 40; tick++)
            {
                Check(
                    player.Y == normal.Players[0].Y && player.VY == normal.Players[0].VY,
                    $"Tapping a tongue jump gives the full held-jump trajectory at {tick}: {player.Y}/{player.VY} vs {normal.Players[0].Y}/{normal.Players[0].VY}"
                );
                Step(world);
                Step(normal, jump);
            }
        }
        var bounced = Pulling(true, true);
        ReplayStep(bounced, jump);
        Check(
            bounced.Players[0].Mode == CharacterMode.Tongue && bounced.Players[0].WasBouncingBeforeTongue,
            "Jump cannot cancel a tumble tongue"
        );
        Check(!bounced.Events.Any(e => e.Kind == SimulationEventKind.Jump), "A tumble tongue emits no jump");
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
            world.Fly.VY = FromDecimal(.05m);
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
        var rules = new GameRules(
            playerCount: 4,
            format: MatchFormat.Teams,
            teams: [0, 0, 1, 1],
            modifiers: new() { FlyTumble = true, FlyExplode = true }
        );
        Fixed previousStrength = 0;
        foreach (int charge in new[] { 0, 1 })
        {
            var world = CreateWorld(rules);
            for (int slot = 1; slot < 4; slot++)
                world.Players[slot].X = slot * 8;
            world.Fly.Active = true;
            world.Fly.X = 2;
            world.Fly.Y = 1;
            Swing(world.Players[0]);
            world.Players[0].AttackCharge = charge;
            ReplayStep(world);
            Check(
                world.Fly.Phase == FlyPhase.Tumbling && world.Fly.LastHitBy == 0,
                "An explosive fly tumbles and credits its last hitter"
            );
            var strength = world.Fly.ExplosionPower;
            Check(strength > previousStrength, "Charging the bat increases explosion strength");
            previousStrength = strength;
            Check(
                world.Events.Single(e => e.Kind == SimulationEventKind.FlyHit).HitstopSeconds == FromDecimal(.5m),
                "An explosive fly retains half a second of launch hitstop and stars"
            );
            var origin = world.Fly.Position;
            ReplayStep(world, count: world.Fly.HitstopTicks);
            Check(world.Fly.Position == origin && world.Fly.Active, "The armed fly freezes before launching");
            world.Fly.Y = 6;
            world.Fly.VY = FromDecimal(.05m);
            ReplayStep(world);
            Check(
                world.Fly.Phase == FlyPhase.Tumbling && world.Fly.VY < 0,
                "An armed fly passes its apex without disarming"
            );

            world.Fly.X = 4;
            world.Fly.Y = FlyState.Radius + FromDecimal(.005m);
            world.Fly.VY = -10;
            foreach (var player in world.Players)
                player.HitstopTicks = 0;
            ReplayStep(world);
            Check(!world.Fly.Active && world.Fly.SpawnTicks > 0, "The next surface contact detonates the fly");
            Check(
                world.Players[0].HitsTaken == 0 && world.Players[0].HitstopTicks == 0,
                "The explosion neither launches nor freezes its owner"
            );
            foreach (var victim in world.Players.Skip(1))
            {
                Check(
                    victim.Mode == CharacterMode.Bouncing && victim.LastHitBy == 0 && victim.HitsTaken == 1,
                    "The blast hits everyone else, including the owner's teammate"
                );
                Check(
                    Fixed.Abs(victim.Velocity.Length - strength) < FromDecimal(.001m),
                    "Blast speed matches the strength of the hit that armed the fly"
                );
                Check(victim.VX > 0 && victim.VY > 0, "Blast knockback points away from the contact point");
            }
            Check(
                world.Events.Count(e => e.Kind == SimulationEventKind.FlyExplode) == 1,
                "A contact produces exactly one explosion"
            );
        }
        foreach (var direction in new FixedVector[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) })
        {
            var world = WithFly(rules.Modifiers);
            world.Fly.Phase = FlyPhase.Tumbling;
            world.Fly.LastHitBy = 0;
            world.Fly.ExplosionPower = 30;
            world.Fly.X = direction.X * 29;
            world.Fly.Y = direction.Y * 17;
            world.Fly.HitstopTicks = 2;
            ReplayStep(world);
            Check(
                !world.Fly.Active && world.Events.Any(e => e.Kind == SimulationEventKind.FlyExplode),
                "An armed fly detonates beyond every screen edge, before the kill bounds"
            );
        }
        var takeover = WithFly(new() { FlyTumble = true, FlyExplode = true });
        Swing(takeover.Players[0]);
        Step(takeover);
        takeover.Fly.X = 22;
        Swing(takeover.Players[1]);
        ReplayStep(takeover);
        Check(
            takeover.Fly.LastHitBy == 1 && takeover.Fly.HitsTaken == 2,
            "Batting an armed fly transfers ownership and increases its power"
        );
        takeover.Fly.HitstopTicks = 0;
        takeover.Fly.Y = FlyState.Radius + FromDecimal(.005m);
        takeover.Fly.VY = -10;
        foreach (var player in takeover.Players)
            player.HitstopTicks = 0;
        ReplayStep(takeover);
        Check(
            takeover.Players[1].HitsTaken == 0 && takeover.Players[0].LastHitBy == 1,
            "Only the newest owner is immune to the explosion"
        );
    }
}
