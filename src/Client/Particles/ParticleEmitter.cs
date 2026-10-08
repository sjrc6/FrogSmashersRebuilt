using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

public sealed class ParticleEmitter
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
    public ParticleEmitterData Data => data;

    public ParticleEmitter(ParticleEmitterData data, uint seed, Vector3? position = null)
    {
        this.data = data;
        origin = position ?? new(data.X, data.Y, data.Z);
        static Vector3 V(float[] a) => new(a[0], a[1], a[2]);
        right = V(data.Right);
        up = V(data.Up);
        forward = V(data.Forward);
        scale = V(data.EmitterScale);
        velocity = V(data.LocalVelocity);
        noiseOffset = ParticleNoise.Offset(seed);
        random = new(seed);
        color = new(data.Color[0], data.Color[1], data.Color[2], data.Color[3]);
        particles = new Particle[
            checked(data.BurstCount[1] + (int)Math.Ceiling(data.Rate * (data.Lifetime + Step)) + 3)
        ];
        if (data.BurstCount[1] > 0)
        {
            int burstCount =
                data.BurstCount[0] + (int)(random.Next() % (uint)(data.BurstCount[1] - data.BurstCount[0] + 1));
            for (int i = 0; i < burstCount; i++)
                Spawn(i + 1, 0, 0);
        }
    }

    public void AdvanceTo(double seconds)
    {
        displayTime = Math.Max(0, seconds) + (data.Prewarm ? data.Duration : 0);
        long target = (long)Math.Ceiling(displayTime / Step - 1e-9);
        if (target < frame)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seconds),
                "Particle time cannot rewind; recreate the emitter for a new scene."
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
            Integrate(ref p, (float)Step, (float)end);
        }

        long total = (long)Math.Floor(end * data.Rate + 1e-9);
        while (births < total)
        {
            long id = ++births;
            double born = id / (double)data.Rate;
            Spawn(id, born, end);
        }
    }

    private void Spawn(long id, double born, double end)
    {
        float radius = MathF.Sqrt(random.NextFloat());
        float angle = random.NextFloat() * MathHelper.TwoPi;
        var disc = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        float depth = data.ConeLength > 0 ? random.NextFloat() * data.ConeLength : 0;
        var position = new Vector3(disc * (data.Radius + depth * MathF.Tan(data.ConeAngle)), depth);
        var v = ConeVelocity(disc, data.ConeAngle, data.Speed);
        if (data.RandomDirection > 0)
        {
            float z = random.NextFloat() * 2 - 1;
            float azimuth = random.NextFloat() * MathHelper.TwoPi;
            float horizontal = MathF.Sqrt(1 - z * z);
            var direction = new Vector3(horizontal * MathF.Cos(azimuth), horizontal * MathF.Sin(azimuth), z);
            v = Vector3.Normalize(Vector3.Lerp(v / data.Speed, direction, data.RandomDirection)) * data.Speed;
        }

        if (count == particles.Length)
            throw new InvalidOperationException("Particle capacity exceeded");
        var particle = new Particle
        {
            Id = id,
            Birth = born,
            Previous = position,
            Position = position,
            Velocity = v + velocity,
            Size = MathHelper.Lerp(data.Size[0], data.Size[1], random.NextFloat()),
        };
        Integrate(ref particle, (float)(end - born), (float)end);
        particles[(first + count++) % particles.Length] = particle;
    }

    private void Integrate(ref Particle particle, float seconds, float time)
    {
        float speed = particle.Velocity.Length();
        if (data.SpeedDamping > 0 && speed > data.SpeedLimit)
        {
            float damping = 1 - MathF.Pow(1 - data.SpeedDamping, seconds / (float)Step);
            particle.Velocity *= MathHelper.Lerp(speed, data.SpeedLimit, damping) / speed;
        }
        var scrollingNoise = noiseOffset + Vector3.UnitZ * (time * data.NoiseScrollSpeed);
        var noise = ParticleNoise.Velocity(
            particle.Position,
            scrollingNoise,
            data.NoiseFrequency,
            data.NoiseStrength,
            data.NoiseDamping
        );
        particle.Position += (particle.Velocity + noise) * seconds;
    }

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
        var tint = color;
        if (data.FadeOut)
            tint.A = (byte)(color.A * (1 - age / data.Lifetime));
        sample = new(
            p.Id,
            origin + right * local.X + up * local.Y + forward * local.Z,
            p.Size * Growth(data, age / data.Lifetime) * scale.X,
            data.Rotation,
            tint
        );
        return true;
    }
}
