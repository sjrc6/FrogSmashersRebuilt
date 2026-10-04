namespace FrogSmashers.Core;

internal sealed class CollisionMap
{
    internal readonly record struct BodyContact(Fixed Fraction, FixedVector Normal, Fixed Surface);

    private readonly Box[] boxes;

    public CollisionMap(MapData map)
    {
        boxes = map
            .Collision.Select(box => new Box(
                Fixed.FromDecimal(box.X - box.Width / 2),
                Fixed.FromDecimal(box.X + box.Width / 2),
                Fixed.FromDecimal(box.Y - box.Height / 2),
                Fixed.FromDecimal(box.Y + box.Height / 2),
                box.OneWay
            ))
            .ToArray();
    }

    public bool Raycast(FixedVector origin, bool horizontal, Fixed distance, bool includeOneWay, out Fixed allowed)
    {
        allowed = distance;
        bool hit = false;
        if (distance == Fixed.Zero)
        {
            return false;
        }

        foreach (var box in boxes)
        {
            if (box.OneWay && !includeOneWay)
            {
                continue;
            }

            var left = box.Left;
            var right = box.Right;
            var bottom = box.Bottom;
            var top = box.Top;
            var transverse = horizontal ? origin.Y : origin.X;
            var lowTransverse = horizontal ? bottom : left;
            var highTransverse = horizontal ? top : right;
            if (transverse < lowTransverse || transverse > highTransverse)
            {
                continue;
            }

            var value = horizontal ? origin.X : origin.Y;
            var low = horizontal ? left : bottom;
            var high = horizontal ? right : top;
            Fixed candidate;
            if (distance > 0)
            {
                if (value >= high)
                {
                    continue;
                }

                candidate = value >= low ? Fixed.Zero : low - value;
                if (candidate > allowed)
                {
                    continue;
                }
            }
            else
            {
                if (value <= low)
                {
                    continue;
                }

                candidate = value <= high ? Fixed.Zero : high - value;
                if (candidate < allowed)
                {
                    continue;
                }
            }

            allowed = candidate;
            hit = true;
        }

        return hit;
    }

    public bool TouchesCircle(FixedVector point, Fixed radius, bool includeOneWay)
    {
        foreach (var box in boxes)
        {
            if (
                (!box.OneWay || includeOneWay)
                && CollisionQueries.CircleTouchesBox(point, radius, box.Left, box.Bottom, box.Right, box.Top)
            )
            {
                return true;
            }
        }

        return false;
    }

    public bool SweepBody(FixedVector position, FixedVector delta, bool includeOneWay, out BodyContact contact)
    {
        contact = default;
        bool hit = false;
        foreach (var box in boxes)
        {
            Fixed time;
            FixedVector side;
            if (box.OneWay)
            {
                if (!includeOneWay || delta.Y >= 0 || position.Y < box.Top)
                    continue;
                time = (box.Top - position.Y) / delta.Y;
                var x = position.X + delta.X * time;
                if (time < 0 || time > 1 || x + 1 <= box.Left || x - 1 >= box.Right)
                    continue;
                side = new(0, 1);
            }
            else
            {
                if (
                    !CollisionQueries.SweepBox(
                        position,
                        delta,
                        box.Left - 1,
                        box.Bottom - 2,
                        box.Right + 1,
                        box.Top,
                        out time,
                        out side
                    )
                )
                    continue;
                if (side == default || FixedVector.Dot(delta, side) >= 0)
                    continue;
                if (delta.X == Fixed.Zero && (position.X + 1 <= box.Left || position.X - 1 >= box.Right))
                    continue;
                if (delta.Y == Fixed.Zero && (position.Y + 2 <= box.Bottom || position.Y >= box.Top))
                    continue;
            }
            if (!hit || time < contact.Fraction)
            {
                var surface =
                    side.Y > 0 ? box.Top
                    : side.Y < 0 ? box.Bottom - 2
                    : side.X > 0 ? box.Right + 1
                    : box.Left - 1;
                contact = new(time, side, surface);
                hit = true;
            }
        }
        return hit;
    }

    public bool TouchesWall(FixedVector position, int direction)
    {
        var side = position.X + direction;
        var tolerance = Fixed.FromDecimal(.001m);
        foreach (var box in boxes)
        {
            if (box.OneWay || position.Y + 2 <= box.Bottom || position.Y >= box.Top)
                continue;
            var distance = direction > 0 ? box.Left - side : side - box.Right;
            if (distance >= -tolerance && distance <= tolerance)
                return true;
        }
        return false;
    }

    public bool SweepTongue(FixedVector start, FixedVector end, bool includeOneWay, out Fixed fraction)
    {
        fraction = 1;
        bool hit = false;
        var radius = Fixed.FromDecimal(.5m);
        foreach (var box in boxes)
        {
            Fixed time;
            if (box.OneWay)
            {
                if (!includeOneWay || end.Y >= start.Y || start.Y < box.Top)
                    continue;
                if (
                    !CollisionQueries.SweepCircleBox(
                        start,
                        end,
                        radius,
                        box.Left,
                        box.Top,
                        box.Right,
                        box.Top,
                        out time
                    )
                )
                    continue;
            }
            else if (
                !CollisionQueries.SweepCircleBox(start, end, radius, box.Left, box.Bottom, box.Right, box.Top, out time)
            )
                continue;
            if (!hit || time < fraction)
            {
                fraction = time;
                hit = true;
            }
        }
        return hit;
    }

    private readonly record struct Box(Fixed Left, Fixed Right, Fixed Bottom, Fixed Top, bool OneWay);
}
