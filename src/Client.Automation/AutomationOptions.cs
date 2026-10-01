namespace FrogSmashers.Client.Automation;

internal sealed class AutomationOptions
{
    public LaunchOptions Game { get; private set; } = null!;
    public int StartPlayers { get; private set; }
    public int StartSpectators { get; private set; }
    public bool Spectate { get; private set; }
    public int Frames { get; private set; }
    public long Ticks { get; private set; }
    public int RenderFps { get; private set; } = 60;
    public string? Capture { get; private set; }
    public string? Result { get; private set; }
    public string? InputScript { get; private set; }

    public static AutomationOptions Parse(string[] arguments)
    {
        var options = new AutomationOptions();
        var gameArguments = new List<string>();
        bool offscreen = false;
        for (int index = 0; index < arguments.Length; index++)
        {
            string Value() =>
                ++index < arguments.Length
                    ? arguments[index]
                    : throw new ArgumentException("Missing value for " + arguments[index - 1]);
            switch (arguments[index])
            {
                case "--spectate":
                    options.Spectate = true;
                    break;
                case "--start-spectators":
                    options.StartSpectators = int.Parse(Value());
                    break;
                case "--start-players":
                    options.StartPlayers = int.Parse(Value());
                    break;
                case "--frames":
                    options.Frames = int.Parse(Value());
                    break;
                case "--ticks":
                    options.Ticks = long.Parse(Value());
                    break;
                case "--render-fps":
                    options.RenderFps = int.Parse(Value());
                    break;
                case "--capture":
                    options.Capture = Value();
                    break;
                case "--result":
                    options.Result = Value();
                    break;
                case "--input-script":
                    options.InputScript = Value();
                    break;
                case "--offscreen":
                    offscreen = true;
                    break;
                default:
                    gameArguments.Add(arguments[index]);
                    break;
            }
        }

        if (options.Frames < 0 || options.Ticks < 0 || options.RenderFps is < 30 or > 1000)
        {
            throw new ArgumentException("Invalid automation timing options.");
        }

        options.Game = LaunchOptions.Parse(gameArguments);
        options.Game.Offscreen = offscreen;
        return options;
    }
}
