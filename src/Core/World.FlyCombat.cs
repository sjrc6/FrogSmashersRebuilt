using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private void ResetFlyTumble()
    {
        Fly.Phase = FlyPhase.Flying;
        Fly.HasBounced = false;
        Fly.HitsTaken = 0;
        Fly.LastHitBy = -1;
        Fly.HitstopTicks = 0;
        Fly.RecoveryTicks = 0;
        Fly.TimeSinceHit = 0;
        Fly.GravityRestore = 0;
        Fly.ExplosionPower = 0;
    }

    private void ResumeFlyFlight()
    {
        ResetFlyTumble();
        FlyDirection();
        Fly.VX = Fly.TargetVX;
        Fly.VY = Fly.TargetVY;
    }

    private void HitFly(PlayerState attacker, FixedVector direction, Fixed power)
    {
        ReleaseFly();
        bool exploding = Rules.Modifiers.FlyExplode;
        Fly.Phase = exploding ? FlyPhase.Exploding : FlyPhase.Tumbling;
        Fly.HasBounced = false;
        Fly.HitsTaken++;
        Fly.LastHitBy = attacker.Slot;
        Fly.TimeSinceHit = 0;
        Fly.GravityRestore = 0;
        Fly.RecoveryTicks = 0;
        Fly.ExplosionPower = exploding ? power : Fixed.Zero;
        var velocity = HitVelocity(direction, Fly.HitsTaken, power, false);
        Fly.VX = velocity.X;
        Fly.VY = velocity.Y;
        var duration = Fly.HitsTaken + power;
        Fly.HitstopTicks = exploding ? TickRate / 2 : Math.Max(Fly.HitstopTicks, HitstopTicks(duration));
        ApplyHitstop(attacker, duration, 0);
        ApplyNearbyHitstop(Fly.Position, duration);
        Emit(
            SimulationEventKind.FlyHit,
            other: attacker.Slot,
            strength: HitPower(Fly.HitsTaken, power, false),
            power: power,
            comboHits: Fly.HitsTaken,
            hitKind: exploding ? HitKind.FlyExplosion : HitKind.Bat,
            position: Fly.Position,
            velocity: velocity,
            hitstopSeconds: (Fixed)Fly.HitstopTicks / TickRate
        );
    }

    private void ExplodeFly(PlayerState attacker, Fixed power)
    {
        var origin = Fly.Position;
        Fly.Active = false;
        Fly.SpawnTicks = NextFlySpawnTicks();
        ResetFlyTumble();
        Emit(SimulationEventKind.FlyExplode, other: attacker.Slot, position: origin, power: power);
        foreach (var victim in Players)
        {
            if (!victim.Alive)
                continue;
            var direction = victim.Center - origin;
            if (direction == default)
                direction = new(0, 1);
            Hit(victim, attacker, direction, power, HitKind.FlyExplosion);
        }
    }

    private void UpdateFlyTumble()
    {
        if (Fly.HitstopTicks > 0)
        {
            Fly.HitstopTicks--;
            if (Fly.HitstopTicks == 0 && Fly.Phase == FlyPhase.Tumbling && Fly.HitsTaken >= 5)
                Emit(SimulationEventKind.FlyLaunch, position: Fly.Position, comboHits: Fly.HitsTaken);
            return;
        }
        if (Fly.Phase == FlyPhase.Exploding)
        {
            ExplodeFly(Players[Fly.LastHitBy], Fly.ExplosionPower);
            return;
        }
        if (Fly.Phase == FlyPhase.Recovering)
        {
            if (--Fly.RecoveryTicks <= 0)
                ResumeFlyFlight();
            return;
        }

        Fly.TimeSinceHit += TickDuration;
        var gravity = BounceGravity(Fly.TimeSinceHit, ref Fly.GravityRestore, TickDuration);
        if (Fly.VY >= 0 && Fly.VY - gravity * TickDuration < 0)
        {
            ResumeFlyFlight();
            return;
        }
        Fly.VY = Fixed.Max(Fly.VY - gravity * TickDuration, tuning.MaxFallSpeed);
        MoveTumblingFly();
    }

    private void MoveTumblingFly()
    {
        var position = Fly.Position;
        var velocity = new FixedVector(Fly.VX, Fly.VY);
        var remaining = TickDuration;
        for (int contact = 0; contact < 4 && remaining > 0; contact++)
        {
            var delta = velocity * remaining;
            if (
                !collisionMaps[Match.CurrentMapIndex]
                    .SweepBall(position, delta, FlyState.Radius, true, out var fraction, out var normal)
            )
            {
                position += delta;
                break;
            }

            position += delta * fraction + normal * FromDecimal(.001m);
            if (Fly.HasBounced && velocity.LengthSquared < 25)
            {
                Fly.Phase = FlyPhase.Recovering;
                Fly.RecoveryTicks = TickRate / 2;
                velocity = default;
                break;
            }

            var impact = FixedVector.Dot(velocity, normal);
            bool floor = normal.Y > FromDecimal(.5m);
            int side =
                floor ? -2
                : normal.Y < -FromDecimal(.5m) ? 2
                : normal.X < 0 ? 1
                : -1;
            Fly.HasBounced = true;
            Emit(
                SimulationEventKind.FlyBounce,
                strength: Fixed.Abs(impact),
                position: position,
                velocity: velocity,
                surfaceSide: side
            );
            var restitution = floor ? FromDecimal(.5m) : Fixed.One;
            velocity -= normal * impact * (1 + restitution);
            remaining *= 1 - fraction;
        }
        Fly.X = position.X;
        Fly.Y = position.Y;
        Fly.VX = velocity.X;
        Fly.VY = velocity.Y;
    }
}
