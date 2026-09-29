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

    public static bool SegmentBox(FixedVector a, FixedVector b, Fixed left, Fixed bottom, Fixed right, Fixed top)
    {
        Fixed near = 0;
        Fixed far = 1;
        bool Slab(Fixed start, Fixed delta, Fixed low, Fixed high)
        {
            if (delta == Fixed.Zero)
            {
                return start >= low && start <= high;
            }

            var entry = (low - start) / delta;
            var exit = (high - start) / delta;
            if (entry > exit)
            {
                (entry, exit) = (exit, entry);
            }

            near = Fixed.Max(near, entry);
            far = Fixed.Min(far, exit);
            return near <= far;
        }

        return Slab(a.X, b.X - a.X, left, right) && Slab(a.Y, b.Y - a.Y, bottom, top);
    }
}
