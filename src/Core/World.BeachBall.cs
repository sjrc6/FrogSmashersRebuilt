using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
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
        if (ball.Resting)
            return;
        ball.TimeSinceHit += TickDuration;
        var gravity = BounceGravity(ball.TimeSinceHit, ref ball.GravityRestore, TickDuration);
        if (ball.VY > tuning.MaxFallSpeed)
            ball.VY -= gravity * TickDuration;
        if (ball.Settling)
            ball.VX = Fixed.MoveTowards(ball.VX, 0, Fixed.Max(1, Fixed.Abs(ball.VX) * 7) * TickDuration);
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
            if (Fixed.Abs(impact) > 5)
            {
                int side =
                    floor ? -2
                    : normal.Y < -FromDecimal(.5m) ? 2
                    : normal.X < 0 ? 1
                    : -1;
                Emit(SimulationEventKind.BeachBallBounce, position: position, velocity: velocity, surfaceSide: side);
            }
            if (floor && ball.TimeSinceHit > tuning.BounceGravityRestoreDelay && Fixed.Abs(impact) < 25)
                ball.Settling = true;
            Fixed restitution = floor ? FromDecimal(.65m) : 1;
            if (Fixed.Abs(impact) < 2)
                restitution = 0;
            velocity -= normal * impact * (1 + restitution);
            if (floor && restitution == 0 && Fixed.Abs(velocity.X) < FromDecimal(.15m))
            {
                velocity = default;
                ball.Resting = true;
                ball.HitsTaken = 0;
                ball.LastHitBy = -1;
                break;
            }
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
        ball.Settling = ball.Resting = false;
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
