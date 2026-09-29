using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal static class AnimationCurve
{
    public static string SampleSprite(List<TimelineKeyData> keys, float time)
    {
        string sprite = keys[0].SpriteId;
        foreach (var key in keys)
        {
            if (key.Time > time)
            {
                break;
            }

            sprite = key.SpriteId;
        }

        return sprite;
    }

    public static ReadOnlySpan<float> Sample(List<TimelineKeyData> keys, float time, Span<float> storage)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        if (time <= keys[0].Time)
        {
            return keys[0].Value;
        }

        for (int i = 1; i < keys.Count; i++)
        {
            var right = keys[i];
            if (time > right.Time)
            {
                continue;
            }

            var left = keys[i - 1];
            if (time == right.Time)
            {
                return right.Value;
            }

            float duration = right.Time - left.Time;
            if (left.Stepped || duration <= 0)
            {
                return left.Value;
            }

            float t = (time - left.Time) / duration;
            var value = storage[..left.Value.Length];
            for (int component = 0; component < value.Length; component++)
            {
                float m0 = component < left.OutSlope.Length ? left.OutSlope[component] * duration : 0;
                float m1 = component < right.InSlope.Length ? right.InSlope[component] * duration : 0;
                value[component] = Hermite(left.Value[component], right.Value[component], m0, m1, t);
            }

            return value;
        }

        return keys[^1].Value;
    }

    private static float Hermite(float a, float b, float m0, float m1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * a + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * b + (t3 - t2) * m1;
    }
}
