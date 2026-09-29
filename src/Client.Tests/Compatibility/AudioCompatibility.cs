using System.Text.Json;
using FrogSmashers.Client;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;

internal static class AudioCompatibility
{
    public static void Run(string content, Action<bool, string> check)
    {
        var spatializer = new AudioSpatializer();

        (Vector3 Position, float Left, float Right)[] reference =
        [
            (new(0, 0, 0), .24543169f, .24543169f),
            (new(20, 0, 0), .03402844f, .33684698f),
            (new(-10, 0, 0), .33254364f, .08910480f),
            (new(10, 10, 0), .12202697f, .31947069f),
            (new(10, 20, 0), .15928491f, .29679955f),
            (new(10, 40, 0), .18790379f, .26434836f),
            (new(0, 20, -10), .24035554f, .24035554f),
            (new(20, 20, -10), .08651534f, .32287967f),
            (new(0, 0, 100), .19529362f, .19529362f),
        ];
        foreach (var expected in reference)
        {
            var actual = spatializer.Gains(expected.Position) * (.5f / MathF.Sqrt(2));
            check(
                Vector2.Distance(actual, new(expected.Left, expected.Right)) < .001f,
                "3D gain differs from measured Unity output at " + expected.Position
            );
        }

        var data = GameContent.Load(Path.Combine(content, "content.json"));
        using var proof = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Compatibility", "AudioFormats.json"))
        );
        var paths = data
            .Sounds.Values.SelectMany(x => x)
            .Concat(
                data.Maps.Concat(data.PresentationScenes.Values).SelectMany(scene => scene.Audio.Select(a => a.Path))
            )
            .Where(path => path.Length > 0)
            .Distinct()
            .ToArray();
        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var clip = DecodedSound.Read(Path.Combine(content, path));
            var metadata = proof.RootElement.GetProperty(name);
            check(
                clip.Channels == metadata.GetProperty("Channels").GetInt32()
                    && clip.SampleRate == metadata.GetProperty("SampleRate").GetInt32()
                    && clip.Pcm.Length / (clip.Channels * 2) == metadata.GetProperty("Frames").GetInt32(),
                "source audio retains reference channels/rate/frame count: " + name
            );
        }

        check(
            data.CharacterVisualParameters["modFlightVolume"] == 1
                && Math.Abs(data.CharacterVisualParameters["flightVelocityVolumeMod"] - .001f) < 1e-8,
            "flight volume uses authored modulation"
        );
        check(
            data.Sounds["flight"][0] == "" && data.FlightAudio.SpatialBlend == 0 && data.FlightAudio.Pan == 0,
            "unassigned first flight clip and 2D source are preserved"
        );
        foreach (string group in new[] { "Knockout1", "Knockout2", "Knockout3", "KnockoutSuicide" })
        {
            check(
                data.SoundSettings[group].PitchVariance == 0 && data.Sounds[group].Length == 1,
                "KO cue must retain its single original clip and fixed unit pitch: " + group
            );
        }

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

        Console.WriteLine(
            $"Unity spatial/downmix calibration, listener position and {paths.Length} referenced source audio clips passed"
        );
    }
}
