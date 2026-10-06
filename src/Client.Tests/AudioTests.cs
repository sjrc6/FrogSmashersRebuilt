using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using static AudioTestData;

internal static class AudioTests
{
    public static void Run(bool backend, Action<bool, string> check)
    {
        Spatial(check);
        SpatialStreaming(check);
        Variations(check);
        Loops(check);
        if (backend)
        {
            StreamBackend(check);
            SpatialBackend(check);
        }
    }

    private static void SpatialStreaming(Action<bool, string> check)
    {
        var random = new Random(731);
        foreach (int channels in new[] { 1, 2 })
        {
            var samples = Enumerable
                .Range(0, 7001 * channels)
                .Select(_ => (short)random.Next(short.MinValue, short.MaxValue))
                .ToArray();
            var decoded = new DecodedSound(Pcm(samples), 48000, channels);
            var data = new SpatialSoundData(decoded);
            foreach (var gains in new[] { Vector2.Zero, new Vector2(.7f), new Vector2(.2f, .9f), new Vector2(1f, .1f) })
            {
                var expected = AudioSpatializer.SpatialPcm(decoded, gains);
                var actual = new byte[expected.Length];
                int frame = 0;
                while (frame < data.Frames)
                {
                    int count = Math.Min(random.Next(1, 700), data.Frames - frame);
                    int rendered = data.Render(frame, gains, actual.AsSpan(frame * 4, count * 4));
                    check(rendered == count, "spatial stream fills each requested chunk");
                    frame += rendered;
                }
                check(actual.SequenceEqual(expected), "streamed positional audio preserves every mixed PCM sample");
            }
        }
    }

    private static void SpatialBackend(Action<bool, string> check)
    {
        var samples = Enumerable
            .Range(0, 24001)
            .Select(i => (short)(Math.Sin(i * Math.Tau * 440 / 48000) * 1000))
            .ToArray();
        var data = new SpatialSoundData(new(Pcm(samples), 48000, 1));
        using var first = new SpatialSoundInstance(data, new(.2f, .4f));
        using var second = new SpatialSoundInstance(data, new(.4f, .2f));
        first.Instance.Volume = second.Instance.Volume = 0;
        first.Instance.Pitch = second.Instance.Pitch = 1;
        first.Instance.Play();
        second.Instance.Play();
        check(
            first.Instance.State == SoundState.Playing && second.Instance.State == SoundState.Playing,
            "positional instances overlap"
        );
        first.Instance.Pause();
        check(
            first.Instance.State == SoundState.Paused && second.Instance.State == SoundState.Playing,
            "pausing one positional voice leaves the other playing"
        );
        first.Instance.Resume();
        for (
            int tick = 0;
            tick < 150 && (first.Instance.State != SoundState.Stopped || second.Instance.State != SoundState.Stopped);
            tick++
        )
        {
            FrameworkDispatcher.Update();
            first.Update();
            second.Update();
            Thread.Sleep(10);
        }
        check(
            first.Instance.State == SoundState.Stopped && second.Instance.State == SoundState.Stopped,
            "finite positional streams finish, including their partial last buffer"
        );
    }

    private static void Spatial(Action<bool, string> check)
    {
        var spatializer = new AudioSpatializer();
        var centered = spatializer.Gains(Vector3.Zero);
        var left = spatializer.Gains(new(-20, 0, 0));
        var right = spatializer.Gains(new(20, 0, 0));
        check(centered.X > 0 && centered.X == centered.Y, "centered sounds have equal audible channel gains");
        check(left.X > left.Y && right.Y > right.X, "sounds pan toward their side of the listener");
        check(
            Vector2.Distance(left, new(right.Y, right.X)) < .00001f,
            "mirroring a sound across the listener swaps its channel gains"
        );
        check(spatializer.Gains(new(0, 0, 490)) == Vector2.Zero, "linear attenuation is silent at maximum distance");
        var relative = spatializer.Gains(new(20, 3, 5), new(10, 3, -5));
        check(
            Vector2.Distance(relative, spatializer.Gains(new(10, 0, 0))) < .00001f,
            "listener translation preserves relative position"
        );
        var center = new Vector2(1 / MathF.Sqrt(2));
        var mono = AudioSpatializer.SpatialPcm(new(Pcm(10000), 48000, 1), center);
        var same = AudioSpatializer.SpatialPcm(new(Pcm(10000, 10000), 48000, 2), center);
        var opposite = AudioSpatializer.SpatialPcm(new(Pcm(10000, -10000), 48000, 2), center);
        check(Math.Abs(Sample(same, 0) - 2 * Sample(mono, 0)) <= 1, "positional stereo downmix sums both channels");
        check(Sample(opposite, 0) == 0 && Sample(opposite, 1) == 0, "opposite stereo channels cancel when downmixed");
    }

    private static void Variations(Action<bool, string> check)
    {
        foreach (int count in new[] { 2, 3, 4, 6, 8 })
        {
            check(
                Enumerable
                    .Range(0, 256)
                    .Select(tick => (int)(Audio.VariationKey((long)tick << 15) % (ulong)count))
                    .Distinct()
                    .Count() == count,
                "reserved event-id bits must not exclude clips from a sound group"
            );
        }
    }

    private static void Loops(Action<bool, string> check)
    {
        var loop = new LoopResampler(new(Pcm(0, 1000, 2000, 3000, 4000), 48000, 1));
        var chunk = new byte[6];
        loop.Render(chunk);
        check(
            Sample(chunk, 0) == 0 && Sample(chunk, 1) == 1000 && Sample(chunk, 2) == 2000 && loop.Position == 3,
            "unit-rate cursor advances one source frame per output frame"
        );
        loop.Rate = -1;
        loop.Render(chunk);
        check(
            Sample(chunk, 0) == 3000 && Sample(chunk, 1) == 2000 && Sample(chunk, 2) == 1000 && loop.Position == 0,
            "negative rate reverses from the existing cursor"
        );
        loop.Rate = -.5f;
        loop.Render(chunk);
        check(
            Sample(chunk, 0) == 0 && Sample(chunk, 1) == 2000 && Sample(chunk, 2) == 4000 && loop.Position == 3.5,
            "reverse interpolation wraps the loop boundary continuously"
        );
        loop.Rate = 0;
        loop.Render(chunk);
        check(
            Sample(chunk, 0) == 3500 && Sample(chunk, 1) == 3500 && Sample(chunk, 2) == 3500 && loop.Position == 3.5,
            "zero pitch holds its sample and cursor"
        );
        loop.Rate = 4;
        check(loop.Rate == 4, "positive pitch supports fourfold playback");
        loop.Rate = -4;
        check(loop.Rate == -4, "negative pitch supports fourfold reverse playback");
        loop.Reset();
        loop.Rate = .5f;
        loop.Render(chunk);
        check(
            Sample(chunk, 1) == 500 && Sample(chunk, 2) == 1000,
            "fractional pitch linearly interpolates source samples"
        );
        var stereo = new LoopResampler(new(Pcm(10, -20, 30, -40, 50, -60), 48000, 2));
        var pair = new byte[8];
        stereo.Render(pair);
        check(
            Sample(pair, 0) == 10 && Sample(pair, 1) == -20 && Sample(pair, 2) == 30 && Sample(pair, 3) == -40,
            "loop resampling preserves source stereo channels"
        );
        var random = new Random(731);
        short[] samples = Enumerable
            .Range(0, 127)
            .Select(_ => (short)random.Next(short.MinValue, short.MaxValue))
            .ToArray();
        var a = new LoopResampler(new(Pcm(samples), 48000, 1));
        var b = new LoopResampler(new(Pcm(samples), 48000, 1));
        double expectedPhase = 0;
        for (int tick = 0; tick < 1000; tick++)
        {
            float rate = (random.Next(-48, 49)) / 16f;
            a.Rate = b.Rate = rate;
            int frames = random.Next(1, 150);
            var whole = new byte[frames * 2];
            a.Render(whole);
            var split = new byte[frames * 2];
            int middle = random.Next(frames + 1) * 2;
            b.Render(split.AsSpan(0, middle));
            b.Render(split.AsSpan(middle));
            expectedPhase = (expectedPhase + rate * frames) % samples.Length;
            if (expectedPhase < 0)
            {
                expectedPhase += samples.Length;
            }

            check(whole.SequenceEqual(split), "stream chunk boundaries change the resampled PCM");
            check(
                Math.Abs(a.Position - expectedPhase) < 1e-9 && a.Position == b.Position,
                "seeded signed-rate cursor deviates from total phase advance"
            );
        }

        Console.WriteLine("Signed flight rate, reversal, interpolation and 1000 seeded stream chunk checks passed");
    }

    private static void StreamBackend(Action<bool, string> check)
    {
        var samples = Enumerable
            .Range(0, 4800)
            .Select(i => (short)(Math.Sin(i * Math.Tau * 440 / 48000) * 1000))
            .ToArray();
        using var loop = new DynamicFlightLoop(new(Pcm(samples), 48000, 1));
        loop.Volume = 0;
        loop.Play();
        foreach (float rate in new[] { 1f, 4f, .125f, 0, -1, -4 })
        {
            loop.Rate = rate;
            for (int i = 0; i < 5; i++)
            {
                FrameworkDispatcher.Update();
                Thread.Sleep(10);
            }

            check(loop.State == SoundState.Playing, "public streaming backend stopped at signed rate " + rate);
        }

        loop.Stop();
        check(loop.State == SoundState.Stopped, "stream stop did not clear playback");
        loop.Play();
        check(loop.State == SoundState.Playing, "stream did not restart after hitstop");
        loop.Pause();
        loop.Pause();
        FrameworkDispatcher.Update();
        check(loop.State == SoundState.Paused, "local pause must hold a flight loop without restarting it");
        loop.Resume();
        check(loop.State == SoundState.Playing, "one resume must release an idempotent flight pause");
        Console.WriteLine("MonoGame streaming backend start/stop and signed-rate checks passed");
    }
}
