using System.Text.Json;
using FrogSmashers.Client;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;

internal static class SmokeCompatibility
{
    public static void Run(string content, Action<bool, string> check)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Compatibility", "SmokeReference.json"))
        );
        var reference = document.RootElement;
        static float F(JsonElement p, string name) => p.GetProperty(name).GetSingle();
        static Vector3 V(JsonElement p, string x, string y, string z) => new(F(p, x), F(p, y), F(p, z));
        float maximumFieldError = 0;
        float maximumMotionError = 0;
        foreach (var point in reference.GetProperty("Field").EnumerateArray())
        {
            var actual = ParticleNoise.Velocity(V(point, "x", "y", "z"), ParticleNoise.Offset(12345), .5f, 1);
            float error = Vector3.Distance(actual, V(point, "ax", "ay", "az"));
            maximumFieldError = Math.Max(maximumFieldError, error);
            check(error < .00001f, "curl field differs from isolated native ParticleSystem output");
        }

        Vector3 position = default;
        int frame = 0;
        foreach (var point in reference.GetProperty("Motion").EnumerateArray())
        {
            int target = (int)F(point, "frame");
            if (target == 1)
            {
                position = new(.123f, .234f, .345f);
                frame = 0;
            }

            var offset = ParticleNoise.Offset(point.GetProperty("seed").GetUInt32());
            while (frame++ < target)
            {
                position +=
                    (
                        new Vector3(1, .2f, 2)
                        + ParticleNoise.Velocity(
                            position,
                            offset,
                            F(point, "frequency"),
                            F(point, "strength"),
                            F(point, "damping") == 1
                        )
                    ) * (1f / 60);
            }

            frame = target;
            float error = Vector3.Distance(position, V(point, "x", "y", "z"));
            maximumMotionError = Math.Max(maximumMotionError, error);
            check(error < .0005f, "integrated smoke path differs from native ParticleSystem output");
        }

        var emitters = GameContent
            .Load(Path.Combine(content, "content.json"))
            .Maps.Single(m => m.Id == "5Skyline")
            .ParticleEmitters;
        float maximumLifetimeError = 0;
        foreach (var point in reference.GetProperty("LifetimeMotion").EnumerateArray())
        {
            int target = (int)F(point, "frame");
            if (target == 1)
            {
                position = new(.123f, .234f, .345f);
                frame = 0;
            }

            var offset = ParticleNoise.Offset(point.GetProperty("seed").GetUInt32());
            while (frame < target)
            {
                position +=
                    (V(point, "vx", "vy", "vz") + ParticleNoise.Velocity(position, offset, .5f, F(point, "strength")))
                    * (1f / 60);
                frame++;
            }

            float error = Vector3.Distance(position, V(point, "x", "y", "z"));
            maximumLifetimeError = Math.Max(maximumLifetimeError, error);
            check(error < .002f, "full-lifetime smoke motion differs visibly from native output");
        }

        foreach (var point in reference.GetProperty("Cone").EnumerateArray())
        {
            var data = emitters.Single(e => e.Name == point.GetProperty("emitter").GetString());
            var velocity = SmokeEmitter.ConeVelocity(
                new Vector2(F(point, "x"), F(point, "y")) / data.Radius,
                data.ConeAngle,
                data.Speed
            );
            check(
                Vector3.Distance(velocity, V(point, "vx", "vy", "vz")) < .000002f,
                "cone direction differs from native emission"
            );
            check(
                Math.Abs(SmokeEmitter.Growth(data, 0) - F(point, "growth")) < .000001f,
                "smoke initial growth differs from native size"
            );
        }

        Console.WriteLine(
            $"Reference smoke: field error {maximumFieldError:G6}; motion error {maximumMotionError:G6}; lifetime error {maximumLifetimeError:G6}"
        );
    }
}
