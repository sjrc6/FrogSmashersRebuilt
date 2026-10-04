namespace FrogSmashers.Core;

internal static class CollisionQueries
{
    public static bool CircleTouchesBox(
        FixedVector center,
        Fixed radius,
        Fixed left,
        Fixed bottom,
        Fixed right,
        Fixed top
    )
    {
        var nearest = new FixedVector(Fixed.Clamp(center.X, left, right), Fixed.Clamp(center.Y, bottom, top));
        return (center - nearest).LengthSquared <= radius * radius;
    }

    public static Fixed PointSegmentDistanceSquared(FixedVector point, FixedVector a, FixedVector b)
    {
        var delta = b - a;
        var deltaTime =
            delta.LengthSquared == Fixed.Zero
                ? Fixed.Zero
                : Fixed.Clamp(FixedVector.Dot(point - a, delta) / delta.LengthSquared, 0, 1);
        return (point - (a + delta * deltaTime)).LengthSquared;
    }

    public static bool SegmentBox(FixedVector a, FixedVector b, Fixed left, Fixed bottom, Fixed right, Fixed top) =>
        SweepBox(a, b - a, left, bottom, right, top, out _, out _);

    public static bool SweepBox(
        FixedVector start,
        FixedVector delta,
        Fixed left,
        Fixed bottom,
        Fixed right,
        Fixed top,
        out Fixed fraction,
        out FixedVector normal
    )
    {
        Fixed near = 0;
        Fixed far = 1;
        FixedVector entryNormal = default;
        bool Slab(Fixed origin, Fixed movement, Fixed low, Fixed high, FixedVector axis)
        {
            if (movement == Fixed.Zero)
            {
                return origin >= low && origin <= high;
            }

            var entry = (low - origin) / movement;
            var exit = (high - origin) / movement;
            if (entry > exit)
            {
                (entry, exit) = (exit, entry);
            }

            if (entry >= near)
            {
                near = entry;
                entryNormal = movement > 0 ? -axis : axis;
            }
            far = Fixed.Min(far, exit);
            return near <= far;
        }

        bool hit = Slab(start.X, delta.X, left, right, new(1, 0)) && Slab(start.Y, delta.Y, bottom, top, new(0, 1));
        fraction = near;
        normal = entryNormal;
        return hit;
    }

    public static bool SweepCircle(
        FixedVector start,
        FixedVector end,
        FixedVector center,
        Fixed radius,
        out Fixed fraction
    )
    {
        fraction = 0;
        var offset = start - center;
        if (offset.LengthSquared <= radius * radius)
            return true;
        var movement = end - start;
        var length = Fixed.Sqrt(movement.LengthSquared);
        if (length == Fixed.Zero)
            return false;
        var direction = movement / length;
        var along = -FixedVector.Dot(offset, direction);
        if (along < 0)
            return false;
        var nearest = offset + direction * along;
        var depth = radius * radius - nearest.LengthSquared;
        if (depth < 0)
            return false;
        var distance = along - Fixed.Sqrt(depth);
        if (distance > length)
            return false;
        fraction = Fixed.Clamp(distance / length, 0, 1);
        return true;
    }

    public static bool SweepCircleBox(
        FixedVector start,
        FixedVector end,
        Fixed radius,
        Fixed left,
        Fixed bottom,
        Fixed right,
        Fixed top,
        out Fixed fraction
    )
    {
        Fixed nearest = 1;
        bool hit = false;
        void Take(bool touches, Fixed time)
        {
            if (touches && (!hit || time < nearest))
            {
                nearest = time;
                hit = true;
            }
        }
        var delta = end - start;
        Take(SweepBox(start, delta, left - radius, bottom, right + radius, top, out var time, out _), time);
        Take(SweepBox(start, delta, left, bottom - radius, right, top + radius, out time, out _), time);
        Take(SweepCircle(start, end, new(left, bottom), radius, out time), time);
        Take(SweepCircle(start, end, new(left, top), radius, out time), time);
        Take(SweepCircle(start, end, new(right, bottom), radius, out time), time);
        Take(SweepCircle(start, end, new(right, top), radius, out time), time);
        fraction = nearest;
        return hit;
    }
}
