using FrogSmashers.Client;

CrashLog.Register();
try
{
    var options = LaunchOptions.Parse(args);
    if (options.Help)
    {
        Console.WriteLine(LaunchOptions.HelpText);
        return;
    }

    using var game = new FrogGame(options);
    using var shutdown = new ShutdownSignal();
    game.Shutdown = shutdown;
    game.Run();
}
catch (Exception exception)
{
    CrashLog.Write(exception);
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
