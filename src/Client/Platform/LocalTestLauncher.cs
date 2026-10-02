using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class LocalTestLauncher
{
    public static void StartGuests(LaunchOptions options)
    {
        if (options.LocalTestCount == 0 || options.LocalTestIndex != 0)
            return;
        var started = new List<Process>();
        try
        {
            for (int index = 1; index < options.LocalTestCount; index++)
            {
                string assembly = Path.Combine(
                    AppContext.BaseDirectory,
                    typeof(FrogGame).Assembly.GetName().Name + ".dll"
                );
                var launch = StartInfo(options, index, Environment.ProcessPath!, assembly);
                started.Add(
                    Process.Start(launch) ?? throw new InvalidOperationException("Could not launch a test guest.")
                );
            }
        }
        catch
        {
            foreach (var child in started)
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            throw;
        }
        finally
        {
            foreach (var child in started)
                child.Dispose();
        }
    }

    internal static ProcessStartInfo StartInfo(LaunchOptions options, int index, string executable, string assembly)
    {
        var launch = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            launch.ArgumentList.Add(assembly);
        foreach (string argument in options.Arguments)
            launch.ArgumentList.Add(argument);
        launch.ArgumentList.Add("--localtest-instance");
        launch.ArgumentList.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return launch;
    }

    internal static Rectangle TileBounds(Rectangle desktop, int count, int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 8);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
        int columns = 1;
        int unit = 0;
        for (int candidate = 1; candidate <= count; candidate++)
        {
            int rows = (count + candidate - 1) / candidate;
            int size = Math.Min(desktop.Width / (candidate * 16), desktop.Height / (rows * 9));
            if (size > unit)
            {
                unit = size;
                columns = candidate;
            }
        }
        if (unit == 0)
            throw new InvalidOperationException("The display is too small to tile these windows.");
        int width = unit * 16;
        int height = unit * 9;
        int rowCount = (count + columns - 1) / columns;
        int left = desktop.Left + (desktop.Width - columns * width) / 2;
        int top = desktop.Top + (desktop.Height - rowCount * height) / 2;
        return new(left + index % columns * width, top + index / columns * height, width, height);
    }
}
