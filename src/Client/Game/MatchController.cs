using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class MatchController : IDisposable
{
    private const double TickSeconds = 1.0 / World.TickRate;
    private readonly GameContent content;
    private readonly Renderer renderer;
    private readonly Audio audio;
    private readonly Controls controls;
    private readonly string? recordingPath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private LocalSeat[] seats = [];
    private InputReplay? replay;
    private string recordingSettings = "";
    private string ambientMap = "";
    private bool recordingSaved;
    private double accumulator;
    private long rollbackCount;
    private long lastAudioTick = -1;
    private long lastVisualTick = -1;
    public World? World { get; private set; }
    public World? PreviousWorld { get; private set; }
    public NetworkSession? Network { get; private set; }
    public bool ReplayPlayback { get; private set; }
    public bool IsMenuBackground { get; private set; }
    public bool Paused { get; set; }
    public string? Error { get; private set; }
    public float Interpolation => (float)Math.Clamp(accumulator / TickSeconds, 0, 1);
    public bool TerminalConfirmed =>
        World != null
        && (
            Network == null
            || Network.AllPeersConfirmed(World.TickNumber - 1)
            || Network.IsTransportFailure && Network.ConfirmedFrame >= World.TickNumber - 1
        );

    public MatchController(
        GameContent content,
        Renderer renderer,
        Audio audio,
        Controls controls,
        string? recordingPath
    )
    {
        this.content = content;
        this.renderer = renderer;
        this.audio = audio;
        this.controls = controls;
        this.recordingPath = recordingPath;
    }

    public void StartLocal(MatchOptions options, IReadOnlyList<LocalSeat> localSeats)
    {
        Close();
        seats = localSeats.ToArray();
        CreateWorld(options);
        replay = recordingPath != null ? InputReplay.Start(World!) : null;
        recordingSettings = JsonSerializer.Serialize(options, jsonOptions);
        ResetPresentation();
    }

    public void StartMenuBackground(uint seed)
    {
        Close();
        IsMenuBackground = true;
        seats = Enumerable.Range(0, 4).Select(index => new LocalSeat(-1, index)).ToArray();
        CreateWorld(
            new MatchOptions
            {
                Seed = seed,
                Rules = new GameRules
                {
                    PlayerCount = 4,
                    MapOrder = [1],
                    // Keep the menu action running without round-end screens.
                    WinScore = int.MaxValue,
                },
            }
        );
        ResetPresentation();
    }

    public MatchOptions StartNetwork(IGameLobby lobby)
    {
        Close();
        var options =
            JsonSerializer.Deserialize<MatchOptions>(lobby.MatchSettingsJson)
            ?? throw new InvalidDataException("Invalid match settings");
        options.Rules.PlayerCount = lobby.PeerSlots.Sum(slots => slots.Length);
        options.Rules.Teams = lobby.PlayerTeams.Concat(Enumerable.Repeat(0, 8 - lobby.PlayerTeams.Length)).ToArray();
        if (options.Rules.TeamMode && lobby.PlayerTeams.Distinct().Count() < 2)
        {
            throw new InvalidDataException("CHOOSE TWO TEAMS");
        }

        options.Rules.Colors = lobby.PlayerColors.Concat(Enumerable.Repeat(0, 8 - lobby.PlayerColors.Length)).ToArray();
        seats = lobby
            .Roster.Players(lobby.LocalPeer)
            .Select(player => new LocalSeat(player.Cpu ? -1 : player.Id, player.Team, player.Color, player.Id))
            .ToArray();
        CreateWorld(options);
        Network = new NetworkSession(
            World!,
            new SessionConfig(
                lobby.PeerSlots,
                lobby.LocalPeer,
                content.ContentHash,
                World!,
                lobby.SessionId,
                activePeers: lobby.PeerIds.ToArray()
            ),
            lobby.CreateTransport()
        );
        ResetPresentation();
        return options;
    }

    public MatchOptions StartReplay(string path)
    {
        Close();
        var options =
            JsonSerializer.Deserialize<MatchOptions>(File.ReadAllText(path + ".json"))
            ?? throw new InvalidDataException("Missing replay settings");
        replay = InputReplay.Load(path);
        ReplayPlayback = true;
        CreateWorld(options);
        World!.Restore(replay.InitialSnapshot);
        PreviousWorld!.Restore(replay.InitialSnapshot);
        ResetPresentation();
        return options;
    }

    private void CreateWorld(MatchOptions options)
    {
        World = new World(content, options.Rules, options.Seed);
        PreviousWorld = new World(content, options.Rules, options.Seed);
    }

    private void ResetPresentation()
    {
        renderer.Reset();
        audio.Reset();
        controls.ClearPendingEdges();
        accumulator = 0;
        Paused = false;
        Error = null;
        ambientMap = "";
        rollbackCount = 0;
        lastAudioTick = -1;
        lastVisualTick = -1;
        recordingSaved = false;
        if (World != null)
        {
            audio.ListenerPosition = new Vector3(World.Map.CameraX, World.Map.CameraY, -10);
        }
    }

    public void Update(double elapsedSeconds, long tickLimit = long.MaxValue)
    {
        if (World == null)
        {
            return;
        }

        Network?.Poll();
        Reconcile();
        if (Network?.Error != null)
        {
            bool completed = World.Phase == MatchPhase.MatchFinished && Network.IsTransportFailure && TerminalConfirmed;
            Error = completed ? null : Network.Error;
            return;
        }

        if (Paused && Network == null)
        {
            return;
        }

        accumulator += elapsedSeconds;
        for (int step = 0; step < 30; step++)
        {
            double tickDuration = TickSeconds * (Network?.FrameDurationMultiplier ?? 1);
            if (accumulator + 1e-9 < tickDuration)
                break;
            if (
                World.Phase == MatchPhase.MatchFinished
                || World.TickNumber >= tickLimit
                || ReplayPlayback && World.TickNumber >= replay!.Frames.Count
            )
            {
                accumulator = 0;
                break;
            }

            if (Network == null)
            {
                AdvanceLocal();
            }
            else if (!AdvanceNetwork())
            {
                accumulator = Math.Min(accumulator, tickDuration);
                break;
            }

            accumulator -= tickDuration;
        }

        if (ambientMap != World.Map.Id)
        {
            ambientMap = World.Map.Id;
            audio.Ambient(World.Map);
        }

        audio.UpdateFlights(World, (float)elapsedSeconds);
    }

    private void AdvanceLocal()
    {
        var world = World!;
        PreviousWorld!.Restore(world.Capture());
        var inputs = ReplayPlayback ? replay!.Frames[(int)world.TickNumber] : ReadInputs();
        world.Tick(inputs);
        if (!ReplayPlayback)
        {
            replay?.Record(inputs, world);
        }
        else if (
            replay!.Checkpoints.TryGetValue((int)world.TickNumber, out var expected)
            && expected != world.HashState()
        )
        {
            throw new InvalidDataException("Replay diverged at " + world.TickNumber);
        }

        renderer.Consume(world.Events, world);
        audio.PlayEvents(world.Events);
    }

    private InputFrame[] ReadInputs()
    {
        var inputs = new InputFrame[seats.Length];
        for (int index = 0; index < seats.Length; index++)
        {
            inputs[index] =
                seats[index].Device < 0 ? BotController.GetInput(World!, index) : controls.Read(seats[index].Device);
        }

        return inputs;
    }

    private bool AdvanceNetwork()
    {
        var network = Network!;
        var inputs = new InputFrame[network.LocalSlots.Length];
        for (int index = 0; index < inputs.Length; index++)
        {
            inputs[index] =
                Paused ? default
                : seats[index].Device < 0 ? BotController.GetInput(World!, network.LocalSlots[index])
                : controls.Read(seats[index].Device, consume: false);
        }

        bool advanced = network.TryAdvance(inputs);
        if (network.LocalInputSubmitted)
        {
            foreach (var seat in seats)
            {
                if (seat.Device >= 0)
                    controls.Read(seat.Device);
            }
        }

        if (!advanced)
            return false;

        PreviousWorld!.Restore(network.PreviousSnapshot);
        Reconcile();
        return true;
    }

    private void Reconcile()
    {
        if (Network == null || World == null)
        {
            return;
        }

        if (Network.RollbackCount != rollbackCount)
        {
            rollbackCount = Network.RollbackCount;
            renderer.Rewind(Network.LastRollbackFromFrame);
            lastVisualTick = Math.Min(lastVisualTick, Network.LastRollbackFromFrame - 1);
            PreviousWorld!.Restore(Network.PreviousSnapshot);
        }

        foreach (var gameEvent in Network.EventsSince(lastVisualTick + 1))
        {
            float age = (float)Math.Max(0, (World.TickNumber - 1 - gameEvent.Tick) * TickSeconds);
            renderer.Consume([gameEvent], World, age);
        }

        lastVisualTick = World.TickNumber - 1;
        audio.PlayEvents(Network.EventsSince(lastAudioTick + 1).Where(item => item.Tick <= Network.ConfirmedFrame));
        lastAudioTick = Network.ConfirmedFrame;
    }

    public void SaveReplay()
    {
        if (recordingSaved || recordingPath == null || ReplayPlayback || replay == null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(recordingPath))!);
        replay.Save(recordingPath);
        File.WriteAllText(recordingPath + ".json", recordingSettings);
        recordingSaved = true;
    }

    public void Close()
    {
        SaveReplay();
        Network?.Dispose();
        Network = null;
        World = null;
        PreviousWorld = null;
        replay = null;
        ReplayPlayback = false;
        IsMenuBackground = false;
        Error = null;
        Paused = false;
        audio.Reset();
        controls.ClearPendingEdges();
    }

    public void Dispose() => Close();
}
