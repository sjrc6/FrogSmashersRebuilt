internal static class Program
{
    private static int checks;

    private static void Check(bool value, string description)
    {
        checks++;
        if (!value)
        {
            throw new Exception(description);
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            string? root = null;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                foreach (
                    string path in new[]
                    {
                        Path.Combine(dir.FullName, "Content"),
                        Path.Combine(dir.FullName, "src", "ContentBuild"),
                    }
                )
                {
                    if (File.Exists(Path.Combine(path, "content.json")))
                    {
                        root = path;
                        break;
                    }
                }

                if (root != null)
                {
                    break;
                }
            }

            Check(root != null, "Content manifest missing");
            bool backend = args.Contains("--audio");
            AudioTests.Run(backend, Check);
            InputTests.Run(Check);
            if (!args.Contains("--skip-compatibility"))
            {
                AudioCompatibility.Run(root!, Check);
                SmokeCompatibility.Run(root!, Check);
            }
            SmokeTests.Run(root!, Check);
            if (args.Contains("--audio"))
            {
                AudioBackendTests.Run(root!, Check);
            }

            Console.WriteLine($"PASS: {checks} client audio/input/smoke checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }
}
