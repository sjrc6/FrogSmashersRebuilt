namespace FrogSmashers.Client;

public sealed class LaunchOptions
{
    public bool Help { get; private set; }
    public bool Demo { get; private set; }
    public bool NoAudio { get; private set; }
    public bool NoIntro { get; private set; }
    public bool Lan { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Players { get; private set; } = 4;
    public int Port { get; private set; } = 24804;
    public int Peers { get; private set; } = 2;
    public int LocalPlayers { get; private set; } = 1;
    public uint Seed { get; private set; } = 1;
    public string? Map { get; private set; }
    public string? Host { get; private set; }
    public string? Join { get; private set; }
    public string? Record { get; private set; }
    public string? Replay { get; private set; }
    public int[]? MapOrder { get; private set; }
    internal bool Offscreen { get; set; }

    public static LaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        var options = new LaunchOptions();
        for (int index = 0; index < arguments.Count; index++)
        {
            string Value()
            {
                if (++index >= arguments.Count)
                {
                    throw new ArgumentException("Missing value for " + arguments[index - 1]);
                }

                return arguments[index];
            }

            switch (arguments[index])
            {
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                case "--demo":
                    options.Demo = true;
                    options.NoIntro = true;
                    break;
                case "--no-audio":
                    options.NoAudio = true;
                    break;
                case "--no-intro":
                    options.NoIntro = true;
                    break;
                case "--width":
                    options.Width = int.Parse(Value());
                    break;
                case "--height":
                    options.Height = int.Parse(Value());
                    break;
                case "--lan":
                    options.Lan = true;
                    break;
                case "--players":
                    options.Players = int.Parse(Value());
                    break;
                case "--map":
                    options.Map = Value();
                    break;
                case "--map-order":
                    options.MapOrder = Value().Split(',').Select(int.Parse).ToArray();
                    break;
                case "--seed":
                    options.Seed = uint.Parse(Value());
                    break;
                case "--port":
                    options.Port = int.Parse(Value());
                    break;
                case "--peers":
                    options.Peers = int.Parse(Value());
                    break;
                case "--local-players":
                    options.LocalPlayers = int.Parse(Value());
                    break;
                case "--host":
                    options.Host = Value();
                    options.NoIntro = true;
                    break;
                case "--join":
                    options.Join = Value();
                    options.NoIntro = true;
                    break;
                case "--record":
                    options.Record = Value();
                    break;
                case "--replay":
                    options.Replay = Value();
                    options.NoIntro = true;
                    break;
                case "+connect_lobby":
                    options.Join = "steam:" + Value();
                    options.NoIntro = true;
                    break;
                default:
                    throw new ArgumentException("Unknown option " + arguments[index] + ". Use --help.");
            }
        }

        if (
            options.Players is < 2 or > 8
            || options.LocalPlayers is < 1 or > 8
            || options.Peers is < 2 or > 8
            || options.Port is < 1024 or > 65535
        )
        {
            throw new ArgumentException("Invalid launch option range.");
        }

        if (
            options.Width != 0 && options.Width is < 320 or > 8192
            || options.Height != 0 && options.Height is < 180 or > 8192
        )
        {
            throw new ArgumentException("Invalid window size.");
        }

        return options;
    }

    public const string HelpText = """
        Frog Smashers Rebuilt
        ./FrogSmashersRebuilt [options]
          --demo --players 2..8       Practice match with CPU players
          --map 1BusStop              First arena (ID or zero-based index)
          --map-order 0,1,2,3,4,5     Custom arena sequence
          --seed 1                   Match random seed
          --host udp|steam --peers 2  Host a private match
          --join udp:127.0.0.1        Join UDP host (--port 24804)
          --join steam:LOBBY_ID       Join a private Steam lobby
          --local-players 1..8        Local players for a network match
          --lan                      Bind UDP host to LAN (default localhost)
          --record match.fsr         Record a local match
          --replay match.fsr         Play and verify recorded inputs
          --width 1920 --height 1080  Set initial window dimensions
          --no-audio                 Disable audio
          --no-intro                 Open menu directly
        Keyboard 1: WASD, T jump, U bat, Y tongue, R strafe.
        Keyboard 2: arrows, M jump, period bat, comma tongue, N strafe.
        Controller: stick/D-pad, A jump, X bat, B tongue, left shoulder strafe.
        F3 diagnostics; F4 collision overlay; F11 fullscreen; Escape pause/menu.
        """;
}
