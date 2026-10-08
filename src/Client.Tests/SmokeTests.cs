using FrogSmashers.Client;
using FrogSmashers.Core;

internal static class SmokeTests
{
    public static void Run(Action<bool, string> check)
    {
        var data = new ParticleEmitterData
        {
            Rate = 12,
            Lifetime = 2,
            Duration = 1,
            Prewarm = true,
            Radius = .5f,
            Speed = 2,
            ConeAngle = .25f,
            LocalVelocity = [1, 0, 0],
            NoiseStrength = .5f,
            NoiseFrequency = .5f,
            Size = [1, 2],
            Growth = [.5f, 1],
            Color = [1, .5f, 0, .75f],
        };
        var frameStepped = new ParticleEmitter(data, 17);
        frameStepped.AdvanceTo(0);
        var singleAdvance = new ParticleEmitter(data, 17);
        for (int i = 0; i <= 1440; i++)
        {
            frameStepped.AdvanceTo(i / 144.0);
        }

        singleAdvance.AdvanceTo(10);
        var before = Visible(frameStepped);
        check(before.Count > 0, "The cadence comparison exercises visible particles");
        check(
            before.SequenceEqual(Visible(singleAdvance)),
            "smoke depends on elapsed time, not draw cadence or skipped draws"
        );
        frameStepped.AdvanceTo(10);
        check(
            before.SequenceEqual(Visible(frameStepped)),
            "paused smoke must retain position, size, color and ordering"
        );
        singleAdvance.AdvanceTo(120);
        check(
            singleAdvance.Count <= Math.Ceiling(data.Rate * data.Lifetime) + 2,
            "particle storage stays bounded across expiry and wraparound"
        );
        Console.WriteLine("Particle cadence, pause and bounded storage passed");
    }

    private static List<ParticleEmitter.Sample> Visible(ParticleEmitter emitter)
    {
        var result = new List<ParticleEmitter.Sample>();
        for (int i = 0; i < emitter.Count; i++)
        {
            if (emitter.TrySample(i, out var sample))
            {
                result.Add(sample);
            }
        }

        return result;
    }
}
