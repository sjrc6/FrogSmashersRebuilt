using FrogSmashers.Client;
using FrogSmashers.Core;

internal static class AudioContentTests
{
    public static void Run(string content, Action<bool, string> check)
    {
        var data = GameContent.Load(Path.Combine(content, "content.json"));
        var paths = data
            .Sounds.Values.SelectMany(clips => clips)
            .Concat(
                data.Maps.Concat(data.PresentationScenes.Values)
                    .SelectMany(scene => scene.Audio.Select(clip => clip.Path))
            )
            .Where(path => path.Length > 0)
            .Distinct()
            .ToArray();
        foreach (string path in paths)
        {
            var clip = DecodedSound.Read(Path.Combine(content, path));
            check(
                clip.Channels is 1 or 2
                    && clip.SampleRate is >= 8000 and <= 48000
                    && clip.Pcm.Length > 0
                    && clip.Pcm.Length % (clip.Channels * 2) == 0,
                "referenced audio decodes to playable PCM: " + path
            );
        }
        Console.WriteLine($"{paths.Length} referenced audio clips decoded successfully");
    }
}
