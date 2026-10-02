using FrogSmashers.Client;

internal static class SettingsSaveTests
{
    public static void Run(Action<bool, string> check)
    {
        int value = 0;
        var saved = new List<int>();
        var queue = new SettingsSaveQueue(() => saved.Add(value));
        for (int press = 0; press < 40; press++)
        {
            value++;
            queue.Request();
            queue.Update(.075);
        }
        check(saved.Count == 0, "holding an adjustment key does not write intermediate settings");
        queue.Update(.5);
        check(saved.SequenceEqual([40]), "quiet period saves the latest settings once");
        queue.Update(5);
        check(saved.Count == 1, "idle updates do not rewrite settings");
        value = 41;
        queue.Request();
        queue.Flush();
        check(saved.SequenceEqual([40, 41]), "leaving settings flushes a pending adjustment immediately");
        queue.Update(1);
        check(saved.Count == 2, "flushing cancels the delayed write");

        string directory = Path.Combine(Path.GetTempPath(), "fsr-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new ClientSettings { Volume = .25f };
            settings.Save(path);
            settings.Volume = .75f;
            settings.Save(path);
            check(ClientSettings.Parse(File.ReadAllText(path)).Volume == .75f, "settings replace an existing save");
            settings.Volume = float.NaN;
            try
            {
                settings.Save(path);
                throw new Exception("Invalid settings unexpectedly serialized");
            }
            catch (ArgumentException) { }
            check(
                ClientSettings.Parse(File.ReadAllText(path)).Volume == .75f,
                "failed serialization preserves the last save"
            );
            check(Directory.GetFiles(directory).Length == 1, "temporary settings files are cleaned up");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
