using FrogSmashers.Client;

try
{
    var options = LaunchOptions.Parse(args);
    if (options.Help)
    {
        Console.WriteLine(LaunchOptions.HelpText);
        return;
    }

    using var game = new FrogGame(options);
    game.Run();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
