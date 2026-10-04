using static FrogSmashers.Core.CollisionQueries;
using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public static class BotController
{
    private static readonly int ActionCycle = World.TicksFromSeconds(1.75m);
    private static readonly int PhaseOffset = World.TicksFromSeconds(.24m);
    private static readonly int JumpCycle = World.TicksFromSeconds(.83m);
    private static readonly int JumpHold = World.TicksFromSeconds(.54m);
    private static readonly int AttackHold = World.TicksFromSeconds(.92m);
    private static readonly int TongueStart = World.TicksFromSeconds(1.34m);
    private static readonly int TongueEnd = World.TicksFromSeconds(1.36m);

    public static InputFrame GetInput(World world, int slot, int? targetSlot = null)
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
            if (
                other == player
                || !other.Alive
                || world.Rules.TeamMode && other.Team == player.Team
                || targetSlot.HasValue && other.Slot != targetSlot.Value
            )
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
        bool descending = TryGetDescent(world.Map, player, target, out var waypoint, out bool dropThrough);
        if (descending && !dropThrough)
        {
            // Brake before the waypoint in the air so we stay clear of the ledge while falling.
            var routeDx = waypoint - player.X - (player.OnGround ? Fixed.Zero : player.VX * FromDecimal(.1m));
            x =
                routeDx > FromDecimal(.2m) ? (sbyte)1
                : routeDx < -FromDecimal(.2m) ? (sbyte)-1
                : (sbyte)0;
        }

        long phase = (world.TickNumber + slot * PhaseOffset) % ActionCycle;
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
        else if (dropThrough)
        {
            y = -1;
            buttons |= InputButtons.Jump;
        }
        else if (
            !descending
            && !(dy < -2 && player.VY <= 0 && HasLanding(world.Map, ahead, player.Y - 1))
            && (dy > 2 || isGapAhead || player.WallSliding)
            && phase % JumpCycle < JumpHold
        )
        {
            buttons |= InputButtons.Jump;
        }

        bool canFight = !descending && HasClearAttack(world.Map, player.Center, target.Center);
        if (canFight && nearestDistanceSquared < 64 && phase < AttackHold)
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

        if (
            canFight
            && nearestDistanceSquared > 50
            && nearestDistanceSquared < 250
            && phase >= TongueStart
            && phase < TongueEnd
        )
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

    private static bool TryGetDescent(
        MapData map,
        PlayerState player,
        PlayerState target,
        out Fixed waypoint,
        out bool dropThrough
    )
    {
        waypoint = player.X;
        dropThrough = false;
        if (target.Y >= player.Y - 2)
        {
            return false;
        }

        bool found = false;
        Fixed highestTop = FromDecimal(map.KillBounds.Bottom);
        foreach (var box in map.Collision)
        {
            var left = FromDecimal(box.X - box.Width / 2);
            var right = FromDecimal(box.X + box.Width / 2);
            var bottom = FromDecimal(box.Y - box.Height / 2);
            var top = FromDecimal(box.Y + box.Height / 2);
            if (
                top <= highestTop
                || target.Y + 2 > top
                || player.Y + 2 < bottom
                || !SegmentBox(player.Center, target.Center, left - 1, bottom - 1, right + 1, top)
            )
            {
                continue;
            }

            // Down+jump must be held until the feet clear the platform's thickness.
            if (
                box.OneWay
                && player.Y >= bottom - FromDecimal(.1m)
                && player.Y <= top + FromDecimal(.1m)
                && HasLanding(map, player.X, bottom - 1)
                && HasLanding(map, player.X + player.VX * FromDecimal(.15m), bottom - 1)
            )
            {
                found = dropThrough = true;
                highestTop = top;
                waypoint = player.X;
                continue;
            }

            Fixed? bestEdge = null;
            Fixed bestCost = 0;
            // Leave room for the frog's width and for braking after walking off the edge.
            for (int side = -1; side <= 1; side += 2)
            {
                var edge = side < 0 ? left - 2 : right + 2;
                if (!HasLanding(map, edge, bottom - 1))
                {
                    continue;
                }

                var cost = Abs(player.X - edge) + Abs(target.X - edge);
                if (bestEdge == null || cost < bestCost)
                {
                    bestEdge = edge;
                    bestCost = cost;
                }
            }

            if (bestEdge is { } destination)
            {
                found = true;
                dropThrough = false;
                highestTop = top;
                waypoint = destination;
            }
        }

        return found;
    }

    private static bool HasLanding(MapData map, Fixed x, Fixed below)
    {
        if (x <= FromDecimal(map.KillBounds.Left) + 2 || x >= FromDecimal(map.KillBounds.Right) - 2)
        {
            return false;
        }

        foreach (var box in map.Collision)
        {
            var top = FromDecimal(box.Y + box.Height / 2);
            if (
                top <= below
                && top > FromDecimal(map.KillBounds.Bottom) + 2
                && x >= FromDecimal(box.X - box.Width / 2) + 1
                && x <= FromDecimal(box.X + box.Width / 2) - 1
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasClearAttack(MapData map, FixedVector from, FixedVector to)
    {
        foreach (var box in map.Collision)
        {
            if (
                !box.OneWay
                && SegmentBox(
                    from,
                    to,
                    FromDecimal(box.X - box.Width / 2),
                    FromDecimal(box.Y - box.Height / 2),
                    FromDecimal(box.X + box.Width / 2),
                    FromDecimal(box.Y + box.Height / 2)
                )
            )
            {
                return false;
            }
        }

        return true;
    }
}
