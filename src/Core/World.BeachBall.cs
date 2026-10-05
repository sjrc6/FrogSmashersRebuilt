using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private const int BeachBallSettlingDurationTicks = 3 * TickRate;

    private void ResetBeachBall()
    {
        BeachBall = new BeachBallState
        {
            Active = Rules.Lobby,
            X = FromDecimal(Map.BeachBallSpawn.X),
            Y = FromDecimal(Map.BeachBallSpawn.Y),
        };
    }

    private void UpdateBeachBall()
    {
        var ball = BeachBall;
        if (!ball.Active)
        {
            if (--ball.ResetTicks <= 0)
                ResetBeachBall();
            return;
        }
        if (ball.HitstopTicks > 0)
        {
            if (--ball.HitstopTicks == 0 && ball.HitsTaken >= 5)
                Emit(SimulationEventKind.BeachBallLaunch, position: ball.Position, comboHits: ball.HitsTaken);
            return;
        }
        if (ball.Phase == BeachBallPhase.Resting)
            return;
        ball.TimeSinceHit += TickDuration;
        var gravity = BounceGravity(ball.TimeSinceHit, ref ball.GravityRestore, TickDuration);
        if (ball.VY >= 0 && ball.VY - gravity * TickDuration < 0)
            ball.HasReachedApex = true;
        if (ball.VY > tuning.MaxFallSpeed)
            ball.VY -= gravity * TickDuration;
        if (ball.Phase is BeachBallPhase.Settling or BeachBallPhase.Passive)
        {
            ball.VX *= FromDecimal(.95m);
            if (Fixed.Abs(ball.VX) < 1)
                ball.VX = 0;
        }
        if (ball.Phase == BeachBallPhase.Settling)
        {
            ball.SettlingTicks = Math.Min(BeachBallSettlingDurationTicks, ball.SettlingTicks + 1);
            if (ball.SettlingTicks == BeachBallSettlingDurationTicks && ball.VX == 0)
            {
                ball.Phase = BeachBallPhase.Passive;
                ball.HitsTaken = 0;
                ball.LastHitBy = -1;
            }
        }
        MoveBeachBall();
        if (
            ball.X < FromDecimal(Map.KillBounds.Left)
            || ball.X > FromDecimal(Map.KillBounds.Right)
            || ball.Y < FromDecimal(Map.KillBounds.Bottom)
            || ball.Y > FromDecimal(Map.KillBounds.Top)
        )
        {
            ball.Active = false;
            ball.ResetTicks = TickRate;
        }
    }

    private void MoveBeachBall()
    {
        var ball = BeachBall;
        var position = ball.Position;
        var velocity = new FixedVector(ball.VX, ball.VY);
        Fixed remaining = TickDuration;
        for (int contact = 0; contact < 4 && remaining > 0; contact++)
        {
            var delta = velocity * remaining;
            if (
                !collisionMaps[Match.CurrentMapIndex]
                    .SweepBall(position, delta, BeachBallState.Radius, out var fraction, out var normal)
            )
            {
                position += delta;
                break;
            }
            position += delta * fraction + normal * FromDecimal(.001m);
            var impact = FixedVector.Dot(velocity, normal);
            bool floor = normal.Y > FromDecimal(.5m);
            Fixed impactSpeed = Fixed.Abs(impact);
            int side =
                floor ? -2
                : normal.Y < -FromDecimal(.5m) ? 2
                : normal.X < 0 ? 1
                : -1;
            Emit(
                SimulationEventKind.BeachBallBounce,
                strength: impactSpeed,
                position: position,
                velocity: velocity,
                surfaceSide: side
            );
            if (floor && ball.HasReachedApex && ball.Phase == BeachBallPhase.Flying)
            {
                ball.Phase = BeachBallPhase.Settling;
                ball.SettlingTicks = 0;
            }
            if (floor && ball.Phase == BeachBallPhase.Passive && impactSpeed < 3)
            {
                velocity = default;
                ball.Phase = BeachBallPhase.Resting;
                break;
            }
            Fixed restitution = floor ? FromDecimal(.65m) : 1;
            if (floor && ball.Phase == BeachBallPhase.Settling && impactSpeed < 30)
                restitution = FromDecimal(.95m);
            if (floor && ball.Phase == BeachBallPhase.Passive)
                restitution = FromDecimal(.4m);

            velocity -= normal * impact * (1 + restitution);
            remaining *= 1 - fraction;
        }
        ball.X = position.X;
        ball.Y = position.Y;
        ball.VX = velocity.X;
        ball.VY = velocity.Y;
    }

    private void HitBeachBall(PlayerState player, FixedVector direction, Fixed power, bool tongue = false)
    {
        var ball = BeachBall;
        if (!tongue)
            ball.HitsTaken++;
        ball.LastHitBy = player.Slot;
        ball.TimeSinceHit = 0;
        ball.GravityRestore = 0;
        ball.Phase = BeachBallPhase.Flying;
        ball.HasReachedApex = false;
        ball.SettlingTicks = 0;
        var velocity = HitVelocity(direction, ball.HitsTaken, power, tongue);
        ball.VX = velocity.X;
        ball.VY = velocity.Y;
        Fixed duration = tongue ? FromDecimal(.75m) : ball.HitsTaken + power;
        ball.HitstopTicks = Math.Max(ball.HitstopTicks, HitstopTicks(duration));
        ApplyHitstop(player, tongue ? FromDecimal(.5m) : duration, 0);
        if (!tongue)
            ApplyNearbyHitstop(ball.Position, duration);
        Emit(
            SimulationEventKind.BeachBallHit,
            other: player.Slot,
            strength: HitPower(ball.HitsTaken, power, tongue),
            power: power,
            comboHits: ball.HitsTaken,
            hitKind: tongue ? HitKind.Tongue : HitKind.Bat,
            position: ball.Position,
            velocity: velocity,
            hitstopSeconds: (Fixed)ball.HitstopTicks / TickRate
        );
    }
}
