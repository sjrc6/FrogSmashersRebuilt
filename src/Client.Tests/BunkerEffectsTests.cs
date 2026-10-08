using FrogSmashers.Client;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;

internal static class BunkerEffectsTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var content = GameContent.Load(Path.Combine(root, "content.json"));
        var map = content.Maps.Single(map => map.Id == "7Showdown");
        var data = map.BunkerEffects!;
        check(
            data != null && map.Sprites.Any(sprite => sprite.SourceId == data.LightSourceId),
            "Bunker effects target the authored light overlay"
        );
        check(content.Maps.Count(map => map.BunkerEffects != null) == 1, "Explosions are specific to the bunker");
        var stepped = new BunkerEffects(data!, 17);
        var batched = new BunkerEffects(data!, 17);
        var steppedEvents = new List<(Vector2 Direction, float Power)>();
        var batchedEvents = new List<(Vector2 Direction, float Power)>();
        bool leftDust = false,
            rightDust = false,
            dimmed = false,
            flashed = false;
        int previousExplosion = -1;
        for (int frame = 0; frame < 128 * 120; frame++)
        {
            stepped.Update(
                1 / 128f,
                (direction, power) =>
                {
                    if (previousExplosion >= 0)
                        check(
                            (frame - previousExplosion) / 128f is >= .99f and <= 5.03f,
                            "Explosions follow the original 1–5 second delay"
                        );
                    previousExplosion = frame;
                    steppedEvents.Add((direction, power));
                }
            );
            if ((frame + 1) % 4 == 0)
                batched.Update(1 / 32f, (direction, power) => batchedEvents.Add((direction, power)));
            flashed |= stepped.LightAlpha > 1;
            dimmed |= stepped.LightAlpha == .2f;
            foreach (var emitter in stepped.Dust)
            {
                if (!emitter.TrySample(0, out var sample))
                    continue;
                leftDust |= sample.Position.X < -8;
                rightDust |= sample.Position.X > 8;
            }
        }
        check(
            steppedEvents.Count > 25 && steppedEvents.SequenceEqual(batchedEvents),
            "Explosion timing and shake are independent of update cadence"
        );
        check(
            steppedEvents.All(e => e.Direction.Length() <= 1.0001f && e.Power is >= .3f and <= 1.5f),
            "Explosions preserve the original shake range"
        );
        check(flashed && dimmed && leftDust && rightDust, "Bunker flashes, dims and sheds dust on both sides");
        check(
            stepped.LightAlpha == batched.LightAlpha && Samples(stepped).SequenceEqual(Samples(batched)),
            "Flicker and dust agree at different update rates"
        );
        var pausedDust = Samples(stepped);
        float pausedLight = stepped.LightAlpha;
        stepped.Update(0, (_, _) => throw new Exception("Paused explosions must not fire"));
        check(
            pausedLight == stepped.LightAlpha && pausedDust.SequenceEqual(Samples(stepped)),
            "Pausing freezes light and dust"
        );

        var burst = new ParticleEmitter(data!.Dust, 42, new(15, 10.1f, 0));
        burst.AdvanceTo(0);
        var birth = Enumerable
            .Range(0, burst.Count)
            .Select(i =>
            {
                burst.TrySample(i, out var p);
                return p;
            })
            .ToArray();
        check(birth.Length is >= 20 and <= 50, "Roof dust emits the source 20–50 particle burst");
        check(
            birth.All(p => p.Position.Y is >= 8.56f and <= 10.1f && Math.Abs(p.Size - .1f) < .0001f),
            "Dust starts inside the downward ceiling cone at its original size"
        );
        burst.AdvanceTo(3.75);
        for (int i = 0; i < birth.Length; i++)
        {
            check(
                burst.TrySample(i, out var p)
                    && p.Position.Y < birth[i].Position.Y - 4
                    && p.Position.Y > birth[i].Position.Y - 15,
                "Dust falls with damped speed and noise"
            );
            check(p.Color.A is >= 62 and <= 64, "Dust fades over its 7.5-second lifetime");
        }
        burst.AdvanceTo(8);
        check(burst.Count == 0, "Dust bursts expire without continuous emission");
        batched.Update(600, (_, _) => { });
        check(batched.Dust.Count() <= 8, "Expired dust bursts are removed during long sessions");
        Console.WriteLine("Bunker timing, flicker, dust, cadence and pause checks passed");
    }

    private static ParticleEmitter.Sample[] Samples(BunkerEffects effects) =>
        effects
            .Dust.SelectMany(emitter =>
                Enumerable
                    .Range(0, emitter.Count)
                    .Select(i =>
                    {
                        emitter.TrySample(i, out var sample);
                        return sample;
                    })
            )
            .ToArray();
}
