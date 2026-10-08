using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

public sealed class BunkerEffects(BunkerEffectsData data, uint seed)
{
    private readonly List<DustBurst> dust = new();
    private ParticleRandom random = new(seed);
    private double time;
    private long frame;
    private float explosionDelay;
    private float flickerDelay;
    private float intensity;
    private bool flickerOn;

    public float LightAlpha { get; private set; } = .2f;
    public IEnumerable<ParticleEmitter> Dust => dust.Select(burst => burst.Emitter);

    private sealed record DustBurst(double Birth, ParticleEmitter Emitter);

    public void Update(float seconds, Action<Vector2, float> explode)
    {
        if (seconds <= 0)
            return;
        time += seconds;
        long target = (long)Math.Floor(time / ParticleEmitter.Step + 1e-6);
        while (frame < target)
        {
            frame++;
            Step(explode);
        }
        dust.RemoveAll(burst => time - burst.Birth >= data.Dust.Lifetime);
        foreach (var burst in dust)
            burst.Emitter.AdvanceTo(time - burst.Birth);
    }

    private void Step(Action<Vector2, float> explode)
    {
        float dt = (float)ParticleEmitter.Step;
        explosionDelay -= dt;
        if (explosionDelay < 0)
        {
            explosionDelay = Range(data.ExplosionDelay[0], data.ExplosionDelay[1]);
            intensity = Range(.3f, 1.3f);
            LightAlpha = 1 + intensity;
            float angle = Range(0, MathHelper.TwoPi);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * MathF.Sqrt(random.NextFloat());
            explode(direction, Range(0, .2f) + intensity);
            float x = Range(-21, -9);
            if (random.NextFloat() < .5f)
                x = -x;
            if (intensity > 1)
                dust.Add(new(frame * ParticleEmitter.Step, new(data.Dust, random.Next(), new(x, 10.1f, 0))));
            return;
        }

        flickerDelay -= dt;
        if (flickerDelay < 0)
        {
            flickerOn = !flickerOn;
            flickerDelay = Range(.01f, .03f);
        }
        intensity = Math.Max(.2f, intensity - dt);
        LightAlpha =
            flickerOn && intensity > .5f ? intensity + .2f
            : intensity > .2f ? Range(.2f, .5f + intensity)
            : .2f;
    }

    private float Range(float min, float max) => MathHelper.Lerp(min, max, random.NextFloat());
}
