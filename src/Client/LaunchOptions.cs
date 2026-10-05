namespace FrogSmashers.Client;

public sealed class LaunchOptions
{
    public bool Help { get; private set; }
    public bool Demo { get; private set; }
    public bool NoAudio { get; private set; }
    public bool NoIntro { get; private set; }
    public bool Lan { get; private set; }
    public int Players { get; private set; } = 4;
    public int Port { get; private set; } = 24804;
    public int Slots { get; private set; } = 8;
    public int LocalPlayers { get; private set; }
    public int LocalTestCount { get; private set; }
    public bool Tile { get; private set; }
    internal int LocalTestIndex { get; private set; }
    internal IReadOnlyList<string> Arguments { get; private set; } = [];
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
        var options = new LaunchOptions { Arguments = arguments.ToArray() };
        bool localTest = false;
        bool localPlayersSpecified = false;
        bool testInstance = false;
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
                case "--slots":
                    options.Slots = int.Parse(Value());
                    break;
                case "--local-players":
                    options.LocalPlayers = int.Parse(Value());
                    localPlayersSpecified = true;
                    break;
                case "--localtest":
                    localTest = true;
                    options.LocalTestCount = int.Parse(Value());
                    break;
                case "--tile":
                    options.Tile = true;
                    break;
                case "--localtest-instance":
                    testInstance = true;
                    options.LocalTestIndex = int.Parse(Value());
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
            || options.LocalPlayers is < 0 or > 8
            || options.Slots is < 2 or > 8
            || options.Port is < 1024 or > 65535
        )
        {
            throw new ArgumentException("Invalid launch option range.");
        }

        if ((options.Tile || testInstance) && !localTest)
            throw new ArgumentException(
                "--tile requires --localtest N; instance indices are assigned by the launcher."
            );
        if (localTest)
        {
            if (!localPlayersSpecified)
                options.LocalPlayers = 1;
            if (
                options.LocalTestCount is < 1 or > 8
                || options.LocalTestIndex < 0
                || options.LocalTestIndex >= options.LocalTestCount
            )
                throw new ArgumentException("--localtest requires 1 to 8 total instances.");
            if (options.LocalTestCount * options.LocalPlayers > options.Slots)
                throw new ArgumentException("Local-test players must fit within --slots (maximum 8).");
            if (options.Host != null || options.Join != null || options.Replay != null || options.Record != null)
                throw new ArgumentException(
                    "--localtest cannot be combined with --host, --join, --replay or --record."
                );
            options.Host = options.LocalTestIndex == 0 ? "udp" : null;
            options.Join = options.LocalTestIndex == 0 ? null : "udp:127.0.0.1";
            options.NoIntro = true;
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
          --host udp|steam --slots 8  Open a lobby with up to eight player slots
          --join udp:127.0.0.1        Join UDP host (--port 24804)
          --join steam:LOBBY_ID       Join a Steam lobby by invite ID
          --local-players 0..8        Explicit local party (default: press to join)
          --localtest 1..8            Launch N total instances in a local UDP lobby
          --tile                     Tile local-test windows across the screen
          --lan                      Bind UDP host to LAN (default localhost)
          --record match.fsr         Record a local match
          --replay match.fsr         Play and verify recorded inputs
          --no-audio                 Disable audio
          --no-intro                 Open menu directly
        Keyboard 1: WASD, T jump, U bat, Y tongue, R strafe.
        Keyboard 2: arrows, M jump, period bat, comma tongue, N strafe.
        Controller: stick/D-pad, A jump, X bat, B tongue, left shoulder strafe.
        F3 diagnostics; F4 collision overlay; F11 fullscreen; Escape pause/menu.
        Windowed mode uses 1280x720; --tile uses smaller borderless test windows.
        Local tests load your preferences but do not save changes.
        """;
}
