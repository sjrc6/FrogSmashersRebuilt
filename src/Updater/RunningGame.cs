using System.ComponentModel;
using System.Diagnostics;

namespace FrogSmashers.Updater;

internal static class RunningGame
{
    public static async Task WaitForExit(string executable, CancellationToken cancellation)
    {
        bool announced = false;
        while (IsRunning(executable))
        {
            if (!announced)
            {
                Console.WriteLine(
                    "Close Frog Smashers Rebuilt to finish updating. Your current game will not be interrupted."
                );
                announced = true;
            }
            await Task.Delay(500, cancellation);
        }
    }

    private static bool IsRunning(string executable)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool running = false;
        foreach (var process in Process.GetProcessesByName("FrogSmashersRebuilt"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, executable, comparison))
                        running = true;
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception error)
                {
                    throw new IOException("Cannot check whether the game is running. Close it and try again.", error);
                }
            }
        }
        return running;
    }

    public static void Launch(string directory, PackageManifest manifest)
    {
        string executable = PackageManifest.SafePath(directory, manifest.GameExecutable);
        if (IsRunning(executable))
            return;
        Process
            .Start(new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = true })
            ?.Dispose();
    }
}
