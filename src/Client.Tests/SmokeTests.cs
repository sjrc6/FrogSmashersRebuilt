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
        var frameStepped = new SmokeEmitter(data, 17);
        frameStepped.AdvanceTo(0);
        check(Visible(frameStepped).Count == 12, "prewarm emits for the configured duration");
        var singleAdvance = new SmokeEmitter(data, 17);
        for (int i = 0; i <= 1440; i++)
        {
            frameStepped.AdvanceTo(i / 144.0);
        }

        singleAdvance.AdvanceTo(10);
        var before = Visible(frameStepped);
        check(before.Count == 24, "expired particles leave the configured steady-state density");
        check(
            before.SequenceEqual(Visible(singleAdvance)),
            "smoke depends on elapsed time, not draw cadence or skipped draws"
        );
        frameStepped.AdvanceTo(10);
        check(
            before.SequenceEqual(Visible(frameStepped)),
            "paused smoke must retain position, size, color and ordering"
        );
        check(before.Zip(before.Skip(1)).All(pair => pair.First.Id > pair.Second.Id), "older smoke draws in front");
        var background = new SmokeEmitter(
            new()
            {
                Rate = 8,
                Lifetime = 2,
                Duration = 4,
                Prewarm = true,
                Z = 3,
                EmitterScale = [1, 1, 0],
                LocalVelocity = [1, 1, 1],
            },
            18
        );
        background.AdvanceTo(0);
        check(Visible(background).Count == 16, "prewarm longer than the lifetime discards expired particles");
        check(
            Visible(background).All(p => Math.Abs(p.Position.Z - 3) < .00001f),
            "background scale flattens rendered Z while retaining three-dimensional local motion"
        );
        var linear = new SmokeEmitter(
            new()
            {
                Rate = 60,
                Lifetime = 1,
                Size = [1, 1],
                LocalVelocity = [1, 0, 0],
            },
            1
        );
        linear.AdvanceTo(.025);
        check(
            Visible(linear).Count == 1 && Math.Abs(Visible(linear)[0].Position.X - 1f / 120) < .000001f,
            "new births interpolate from their actual substep birth time"
        );
        linear.AdvanceTo(120);
        check(
            Visible(linear).Count == 60 && linear.Count <= 62,
            "particle storage stays bounded across expiry and wraparound"
        );
        Console.WriteLine("Smoke emission, prewarm, pause and interpolation passed");
    }

    private static List<SmokeEmitter.Sample> Visible(SmokeEmitter emitter)
    {
        var result = new List<SmokeEmitter.Sample>();
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
