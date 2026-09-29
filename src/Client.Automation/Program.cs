using System.Runtime.InteropServices;
using FrogSmashers.Client;
using FrogSmashers.Client.Automation;

try
{
    var options = AutomationOptions.Parse(args);
    if (options.Game.Help)
    {
        Console.WriteLine(LaunchOptions.HelpText);
        Console.WriteLine(
            "Automation: --frames N --render-fps N --ticks N --capture PATH --result PATH --input-script PATH --offscreen"
        );
        return;
    }

    if (options.Game.Offscreen)
    {
        NativeEnvironment.Set("SDL_VIDEODRIVER", "offscreen");
        NativeEnvironment.Set("LIBGL_ALWAYS_SOFTWARE", "1");
        NativeEnvironment.Set("ALSOFT_DRIVERS", "null");
    }

    using var game = new AutomatedGame(options);
    game.Run();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

internal static class NativeEnvironment
{
    [DllImport("libc", EntryPoint = "setenv")]
    private static extern int SetUnix(string name, string value, int overwrite);

    public static void Set(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (OperatingSystem.IsLinux() && SetUnix(name, value, 1) != 0)
        {
            throw new InvalidOperationException("Could not set native environment: " + name);
        }
    }
}
