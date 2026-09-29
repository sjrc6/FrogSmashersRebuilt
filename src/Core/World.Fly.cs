using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private bool TryClaimFly(PlayerState claimant)
    {
        if (!Fly.Active || Fly.Owner >= 0 && Fly.Owner != claimant.Slot)
        {
            return false;
        }

        Fly.Owner = claimant.Slot;
        Fly.ClaimTicks = 3 * TickRate;
        return true;
    }

    private void ReleaseFly()
    {
        Fly.Owner = -1;
        Fly.ClaimTicks = 0;
    }

    private void FlyDirection()
    {
        Fly.DirectionTicks = RandomRange(3 * TickRate, 10 * TickRate);
        if (RandomRange(0, 10) == 0)
        {
            Fly.TargetVX = 0;
            Fly.TargetVY = 0;
            Fly.DirectionTicks = RandomRange(TickRate, 3 * TickRate);
        }
        else
        {
            var direction =
                new FixedVector(RandomRange(-10000, 10001), RandomRange(-10000, 10001)).Normalized * FromDecimal(7.5m);
            Fly.TargetVX = direction.X;
            Fly.TargetVY = direction.Y;
        }
    }

    private void UpdateFly()
    {
        if (!Fly.Active)
        {
            if (Fly.IngestedBy >= 0 || IsShowdown || Phase != MatchPhase.Playing)
            {
                return;
            }

            if (--Fly.SpawnTicks > 0)
            {
                return;
            }

            Fly.Active = true;
            Fly.X = FromDecimal(Map.FlySpawn.X);
            Fly.Y = FromDecimal(Map.FlySpawn.Y);
            var initial = new FixedVector(RandomRange(-10000, 10001), RandomRange(-10000, 10001)).Normalized * 10;
            Fly.VX = initial.X;
            Fly.VY = initial.Y;
            FlyDirection();
            Emit(SimulationEventKind.FlySpawn);
        }

        if (--Fly.DirectionTicks <= 0)
        {
            FlyDirection();
        }

        if (Fly.Owner >= 0)
        {
            var owner = Players[Fly.Owner];
            if (
                !owner.Alive
                || owner.Mode != CharacterMode.Tongue
                || owner.TonguePhase != TonguePhase.RetractingHitFly
                || --Fly.ClaimTicks <= 0
            )
            {
                ReleaseFly();
            }
        }

        if (Fly.Owner < 0)
        {
            var velocity = new FixedVector(Fly.VX, Fly.VY);
            var difference = new FixedVector(Fly.TargetVX, Fly.TargetVY) - velocity;
            var step = 5 * TickDuration;
            velocity += difference.LengthSquared <= step * step ? difference : difference.Normalized * step;
            Fly.VX = velocity.X;
            Fly.VY = velocity.Y;
            var dx = Fly.VX * TickDuration;
            var dy = (Fly.VY + FlyBob() * 3) * TickDuration;
            if (dx != Fixed.Zero && Ray(Fly.Position, true, dx + (dx > 0 ? 1 : -1), false, out _))
            {
                dx = 0;
                Fly.VX *= -FromDecimal(.5m);
                FlyDirection();
            }

            if (dy != Fixed.Zero && Ray(Fly.Position, false, dy + (dy > 0 ? 1 : -1), false, out _))
            {
                dy = 0;
                Fly.VY *= -FromDecimal(.5m);
                FlyDirection();
            }

            Fly.X += dx;
            Fly.Y += dy;
        }

        if (
            Fly.X < FromDecimal(Map.KillBounds.Left)
            || Fly.X > FromDecimal(Map.KillBounds.Right)
            || Fly.Y < FromDecimal(Map.KillBounds.Bottom)
            || Fly.Y > FromDecimal(Map.KillBounds.Top)
        )
        {
            Fly.Active = false;
            ReleaseFly();
            Fly.SpawnTicks = RandomRange(15 * TickRate, 45 * TickRate);
        }
    }

    private Fixed FlyBob()
    {
        var pi = FromDecimal(3.1415926535897932384626433833m);
        var phase = new Fixed((long)(((Int128)TickNumber * 8 * Fixed.Unit / TickRate) % (2 * pi).Raw));
        var sign = phase > pi ? -1 : 1;
        if (sign < 0)
        {
            phase -= pi;
        }

        var product = phase * (pi - phase);
        return sign * 16 * product / (5 * pi * pi - 4 * product);
    }
}
