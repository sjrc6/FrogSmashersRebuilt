using System.Runtime.InteropServices;

namespace FrogSmashers.Client;

internal static class CrashLog
{
    public static string DirectoryPath => Path.Combine(Path.GetDirectoryName(ClientSettings.FilePath)!, "crashlogs");

    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                Write(exception);
        };
    }

    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var now = DateTime.UtcNow;
            string file = Path.Combine(DirectoryPath, $"crash-{now:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(
                file,
                $"Frog Smashers Rebuilt — {now:O}\n"
                    + $"Version: {typeof(FrogGame).Assembly.GetName().Version}\n"
                    + $"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})\n"
                    + $"Runtime: {RuntimeInformation.FrameworkDescription}\n\n"
                    + exception
            );
            foreach (
                string old in Directory
                    .EnumerateFiles(DirectoryPath, "crash-*.txt")
                    .OrderDescending(StringComparer.Ordinal)
                    .Skip(5)
            )
                File.Delete(old);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Could not save crash log: " + error.Message);
        }
    }
}
