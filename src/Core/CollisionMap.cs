namespace FrogSmashers.Core;

internal sealed class CollisionMap
{
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

    public bool Raycast(
        FixedVector origin,
        bool horizontal,
        Fixed distance,
        bool includeOneWay,
        bool preservePlatformEmbedding,
        out Fixed allowed
    )
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

            if (box.OneWay && !preservePlatformEmbedding && value < high)
            {
                continue;
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

    private readonly record struct Box(Fixed Left, Fixed Right, Fixed Bottom, Fixed Top, bool OneWay);
}
