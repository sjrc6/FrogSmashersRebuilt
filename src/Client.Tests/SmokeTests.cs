using FrogSmashers.Client;
using FrogSmashers.Core;

internal static class SmokeTests
{
    public static void Run(string content, Action<bool, string> check)
    {
        var emitters = GameContent
            .Load(Path.Combine(content, "content.json"))
            .Maps.Single(map => map.Id == "5Skyline")
            .ParticleEmitters;
        var foreground = emitters.Single(e => e.Name == "Particle System");
        var a = new SmokeEmitter(foreground, 1369906688);
        a.AdvanceTo(0);
        check(Visible(a).Count == 375, "foreground prewarms its five-second duration");
        var b = new SmokeEmitter(foreground, 1369906688);
        for (int i = 0; i <= 1440; i++)
        {
            a.AdvanceTo(i / 144.0);
        }

        b.AdvanceTo(10);
        var before = Visible(a);
        check(before.Count == 750, "foreground retains its ten-second density");
        check(before.SequenceEqual(Visible(b)), "smoke depends on elapsed time, not draw cadence or skipped draws");
        a.AdvanceTo(10);
        check(before.SequenceEqual(Visible(a)), "paused smoke must retain position, size, color and ordering");
        check(before.Zip(before.Skip(1)).All(pair => pair.First.Id > pair.Second.Id), "older smoke draws in front");
        var background = new SmokeEmitter(emitters.Single(e => e.Name == "Particle System (1)"), 1369906689);
        background.AdvanceTo(0);
        check(Visible(background).Count == 5000, "background prewarms its full fifty-second lifetime");
        check(
            Visible(background).All(p => Math.Abs(p.Position.Z - 8.57f) < .00001f),
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
