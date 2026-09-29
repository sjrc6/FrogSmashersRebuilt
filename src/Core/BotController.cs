namespace FrogSmashers.Core;

public static class BotController
{
    public static InputFrame GetInput(World world, int slot)
    {
        var player = world.Players[slot];
        if (!player.Alive)
        {
            return default;
        }

        PlayerState? target = null;
        Fixed nearestDistanceSquared = 1_000_000;
        foreach (var other in world.Players)
        {
            if (other == player || !other.Alive || world.Rules.TeamMode && other.Team == player.Team)
            {
                continue;
            }

            var distance = (other.Position - player.Position).LengthSquared;
            if (distance < nearestDistanceSquared)
            {
                nearestDistanceSquared = distance;
                target = other;
            }
        }

        if (target == null)
        {
            return default;
        }

        var dx = target.X - player.X;
        var dy = target.Y - player.Y;
        sbyte x =
            dx > Fixed.FromDecimal(.8m) ? (sbyte)1
            : dx < -Fixed.FromDecimal(.8m) ? (sbyte)-1
            : (sbyte)0;
        sbyte y = 0;
        InputButtons buttons = 0;
        long phase = (world.TickNumber + slot * 29) % 210;
        bool isGapAhead = true;
        var ahead = player.X + x * 3;
        foreach (var box in world.Map.Collision)
        {
            var top = Fixed.FromDecimal(box.Y + box.Height / 2);
            if (
                ahead >= Fixed.FromDecimal(box.X - box.Width / 2)
                && ahead <= Fixed.FromDecimal(box.X + box.Width / 2)
                && top <= player.Y + 1
                && top >= player.Y - 5
            )
            {
                isGapAhead = false;
            }
        }

        if (player.Mode == CharacterMode.Bouncing)
        {
            if (player.CanBounceDodge)
            {
                buttons |= InputButtons.Jump;
            }
        }
        else if ((dy > 2 || isGapAhead || player.WallSliding) && phase % 100 < 65)
        {
            buttons |= InputButtons.Jump;
        }

        if (nearestDistanceSquared < 64 && phase < 110)
        {
            buttons |= InputButtons.Attack;
            if (dy > 3)
            {
                y = 1;
            }
            else if (dy < -3 && !player.OnGround)
            {
                y = -1;
            }

            if (Fixed.Abs(dx) < 2)
            {
                x = 0;
            }
        }

        if (nearestDistanceSquared > 50 && nearestDistanceSquared < 250 && phase is > 160 and < 163)
        {
            buttons |= InputButtons.Tongue;
            if (dy > 3)
            {
                y = 1;
            }
            else if (dy < -3 && !player.OnGround)
            {
                y = -1;
            }
        }

        return new(x, y, buttons);
    }
}
