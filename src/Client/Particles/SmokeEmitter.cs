using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

public sealed class SmokeEmitter
{
    public const double Step = 1.0 / 60;
    private readonly ParticleEmitterData data;
    private readonly Vector3 origin;
    private readonly Vector3 right;
    private readonly Vector3 up;
    private readonly Vector3 forward;
    private readonly Vector3 scale;
    private readonly Vector3 velocity;
    private readonly Vector3 noiseOffset;
    private readonly Color color;
    private readonly Particle[] particles;
    private ParticleRandom random;
    private long frame;
    private long births;
    private int first;
    private int count;
    private double displayTime;

    private struct Particle
    {
        public long Id;
        public double Birth;
        public Vector3 Previous;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Size;
    }

    public readonly record struct Sample(long Id, Vector3 Position, float Size, float Rotation, Color Color);

    public int Count => count;

    public SmokeEmitter(ParticleEmitterData data, uint seed)
    {
        this.data = data;
        origin = new(data.X, data.Y, data.Z);
        static Vector3 V(float[] a) => new(a[0], a[1], a[2]);
        right = V(data.Right);
        up = V(data.Up);
        forward = V(data.Forward);
        scale = V(data.EmitterScale);
        velocity = V(data.LocalVelocity);
        noiseOffset = ParticleNoise.Offset(seed);
        random = new(seed);
        color = new(data.Color[0], data.Color[1], data.Color[2], data.Color[3]);
        particles = new Particle[checked((int)Math.Ceiling(data.Rate * (data.Lifetime + Step)) + 3)];
    }

    public void AdvanceTo(double seconds)
    {
        displayTime = Math.Max(0, seconds) + (data.Prewarm ? data.Duration : 0);
        long target = (long)Math.Ceiling(displayTime / Step - 1e-9);
        if (target < frame)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                "Smoke time cannot rewind; recreate the emitter for a new scene."
            );
        }

        while (frame < target)
        {
            AdvanceFrame();
        }
    }

    private void AdvanceFrame()
    {
        double end = ++frame * Step;

        while (count > 0 && particles[first].Birth + data.Lifetime + Step < end)
        {
            first = (first + 1) % particles.Length;
            count--;
        }

        for (int i = 0; i < count; i++)
        {
            ref var p = ref particles[(first + i) % particles.Length];
            p.Previous = p.Position;
            p.Position = Integrate(p.Position, p.Velocity, (float)Step);
        }

        long total = (long)Math.Floor(end * data.Rate + 1e-9);
        while (births < total)
        {
            long id = ++births;
            double born = id / (double)data.Rate;
            float radius = MathF.Sqrt(random.NextFloat());
            float angle = random.NextFloat() * MathHelper.TwoPi;
            var disc = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var position = new Vector3(disc * data.Radius, 0);
            var v = ConeVelocity(disc, data.ConeAngle, data.Speed) + velocity;
            if (count == particles.Length)
            {
                throw new InvalidOperationException("Smoke particle capacity exceeded");
            }

            particles[(first + count++) % particles.Length] = new()
            {
                Id = id,
                Birth = born,
                Previous = position,
                Position = Integrate(position, v, (float)(end - born)),
                Velocity = v,
                Size = MathHelper.Lerp(data.Size[0], data.Size[1], random.NextFloat()),
            };
        }
    }

    private Vector3 Integrate(Vector3 position, Vector3 v, float seconds) =>
        position
        + (
            v
            + ParticleNoise.Velocity(position, noiseOffset, data.NoiseFrequency, data.NoiseStrength, data.NoiseDamping)
        ) * seconds;

    public static Vector3 ConeVelocity(Vector2 unitDisc, float angle, float speed) =>
        Vector3.Normalize(new Vector3(unitDisc * MathF.Tan(angle), 1)) * speed;

    public static float Growth(ParticleEmitterData data, float normalizedAge)
    {
        float t = Math.Clamp(
            (normalizedAge - data.GrowthStartTime) / (data.GrowthEndTime - data.GrowthStartTime),
            0,
            1
        );

        return MathHelper.Lerp(data.Growth[0], data.Growth[1], t * t * (3 - 2 * t));
    }

    public bool TrySample(int newestIndex, out Sample sample)
    {
        sample = default;
        if ((uint)newestIndex >= count)
        {
            return false;
        }

        var p = particles[(first + count - 1 - newestIndex) % particles.Length];
        float age = (float)(displayTime - p.Birth);
        if (age < 0 || age >= data.Lifetime)
        {
            return false;
        }

        double end = frame * Step;
        double start = Math.Max(end - Step, p.Birth);
        float alpha = end > start ? (float)Math.Clamp((displayTime - start) / (end - start), 0, 1) : 0;
        var local = Vector3.Lerp(p.Previous, p.Position, alpha) * scale;
        sample = new(
            p.Id,
            origin + right * local.X + up * local.Y + forward * local.Z,
            p.Size * Growth(data, age / data.Lifetime) * scale.X,
            data.Rotation,
            color
        );
        return true;
    }
}
