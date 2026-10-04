using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public class FrogGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly DisplaySettings display;
    private readonly FrameTiming timing = new();
    private WindowsWindowIcons? windowIcons;
    private MenuRenderer menuRenderer = null!;
    private ToastRenderer toastRenderer = null!;
    internal ConnectionOverlay Connections { get; private set; } = null!;
    private int matchGeneration;
    private bool showDiagnostics;
    private World? diagnosticWorld;
    private long diagnosticTick;
    private double diagnosticSeconds;
    private double diagnosticTickRate;
    private bool contentLoaded;
    private bool connectionFromMainMenu;
    private LobbyPlayer[] connectionParty = [];
    internal LaunchOptions Options { get; }
    internal ShutdownSignal? Shutdown { get; set; }
    internal ClientSettings Settings { get; } = ClientSettings.Load();
    internal Controls Controls { get; }
    internal Assets Assets { get; private set; } = null!;
    internal Renderer Renderer { get; private set; } = null!;
    internal Audio Audio { get; private set; } = null!;
    internal CinematicPlayer Cinematics { get; private set; } = null!;
    internal MatchSetup Setup { get; private set; } = null!;
    internal LobbyController Lobby { get; private set; } = null!;
    internal MatchController Match { get; private set; } = null!;
    internal OnlineController Online { get; private set; } = null!;
    internal MenuController Menus { get; private set; } = null!;
    internal ToastController Toasts { get; } = new();
    internal Task<bool?> FirewallCheck { get; set; } = Ipv6FirewallCheck.Start();
    internal string? LastError { get; private set; }
    internal int RenderedFrames { get; private set; }
    internal bool Fullscreen => graphics.IsFullScreen;
    internal bool HardwareModeSwitch => graphics.HardwareModeSwitch;
    protected virtual bool LimitFrameRate => true;
    protected virtual bool SaveSettingsOnExit => true;
    protected virtual long TickLimit => long.MaxValue;
    private readonly SettingsSaveQueue settingsSave;

    public FrogGame(LaunchOptions options)
    {
        settingsSave = new(WriteSettings);
        Options = options;
        if (options.LocalTestCount > 0)
            Settings.Fullscreen = false;
        Rectangle? tile = options.Tile
            ? LocalTestLauncher.TileBounds(DesktopArea.Primary(), options.LocalTestCount, options.LocalTestIndex)
            : null;
        Controls = new Controls(Settings);
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = tile?.Width ?? Renderer.Width,
            PreferredBackBufferHeight = tile?.Height ?? Renderer.Height,
            SynchronizeWithVerticalRetrace = !options.Offscreen && Settings.VSync,
            IsFullScreen = !options.Offscreen && Settings.Fullscreen,
            HardwareModeSwitch = false,
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        display = new DisplaySettings(Window, graphics, Settings, options.Offscreen, tile);
        Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
        IsMouseVisible = true;
        Window.AllowUserResizing = false;
        Window.Title =
            options.LocalTestCount == 0
                ? "Frog Smashers Rebuilt"
                : $"Frog Smashers Rebuilt - Local Test {options.LocalTestIndex + 1}/{options.LocalTestCount}"
                    + (options.LocalTestIndex == 0 ? " (Host)" : "");
        Window.TextInput += (_, input) => Menus?.EnterText(input.Character);
        Window.ClientSizeChanged += (_, _) => Controls.SuspendMouseHover();
    }

    protected override void LoadContent()
    {
        display.MatchDesktopBackBuffer();
        display.PositionWindow();
        if (OperatingSystem.IsWindows())
            windowIcons = new WindowsWindowIcons(Window.Handle);
        Assets = new Assets(Content, Content.RootDirectory);
        Setup = new MatchSetup(Options.Seed, ResolveFirstMap());
        Renderer = new Renderer(GraphicsDevice, Assets) { ShakeEnabled = Settings.ScreenShake };
        Audio = new Audio(Assets, !Options.NoAudio) { Volume = Settings.Volume, TitleVolume = Settings.TitleVolume };
        Cinematics = new CinematicPlayer(GraphicsDevice, Assets, Audio) { ShakeEnabled = Settings.ScreenShake };
        Match = new MatchController(Assets.Data, Renderer, Audio, Controls, Options.Record);
        Online = new OnlineController(Options, Assets.Data.ComputeGameplayHash());
        Lobby = new LobbyController(this);
        Menus = new MenuController(this);
        menuRenderer = new MenuRenderer(this, Menus);
        toastRenderer = new ToastRenderer(this);
        Connections = new(this);
        ConfigureLaunch();
        if (LastError == null && Online.Lobby != null)
            LocalTestLauncher.StartGuests(Options);
        contentLoaded = true;
    }

    private int ResolveFirstMap()
    {
        if (Options.Map == null)
            return 0;
        int firstMap = int.TryParse(Options.Map, out int index)
            ? index
            : Assets.Data.Maps.FindIndex(map => map.Id.Equals(Options.Map, StringComparison.OrdinalIgnoreCase));
        if (firstMap < 0 || firstMap >= Assets.Data.Maps.Count)
            throw new ArgumentException("Unknown map: " + Options.Map);
        return firstMap;
    }

    private void ConfigureLaunch()
    {
        if (Options.Host != null || Options.Join != null)
        {
            for (int index = 0; index < Options.LocalPlayers; index++)
            {
                Setup.Lobby.Join(Options.Demo && Options.Host != null ? -1 : index);
            }

            if (Options.Host != null)
                Setup.Lobby.Roster.ConfigureEmpty(
                    Options.Host == "steam" ? SlotType.Private : SlotType.Open,
                    Options.Slots
                );
            BeginLobby(Options.Host ?? Options.Join!, Options.Host != null);
        }
        else if (Options.Replay != null)
        {
            Match.StartReplay(Options.Replay);
            ShowMatch();
        }
        else if (Options.Demo)
        {
            for (int index = 0; index < Options.Players; index++)
            {
                Setup.Lobby.Join(-1, index % 2);
            }

            StartLocal();
        }
        else if (Options.NoIntro)
        {
            MainMenu();
        }
        else
        {
            Cinematics.StartIntro();
        }
    }

    protected virtual double ElapsedSeconds(GameTime gameTime) => Math.Min(0.25, gameTime.ElapsedGameTime.TotalSeconds);

    protected override void Update(GameTime gameTime)
    {
        if (Shutdown?.Requested == true)
        {
            Exit();
            return;
        }
        Controls.Poll(IsActive || Options.Offscreen);
        double elapsedSeconds = ElapsedSeconds(gameTime);
        Toasts.Update(elapsedSeconds);
        PumpNetwork();
        CheckLobbyReturn();
        HandleGlobalInput();
        Renderer.Update(Menus.LocalPresentationPaused ? 0 : (float)elapsedSeconds);
        Audio.SetPaused(Menus.LocalPresentationPaused);
        Audio.Update();
        if (Menus.ShowingCinematic)
        {
            Cinematics.Update((float)elapsedSeconds);
        }

        settingsSave.Update(elapsedSeconds);
        Menus.Update(elapsedSeconds);
        if (Match.IsMenuBackground)
        {
            if (Menus.ShowingMenuBackground)
            {
                Match.Update(elapsedSeconds);
            }
            else
            {
                Match.Close();
            }
        }

        CheckInvitations();
        base.Update(gameTime);
    }

    private void PumpNetwork()
    {
        Online.Lobby?.SetRollbackSettings(Settings.Rollback);
        Online.Lobby?.Poll();
        Match.Network?.SetTiming(Settings.Rollback);
        Match.Network?.Poll();
    }

    private void HandleGlobalInput()
    {
        if (Menus.WaitingForBinding)
            return;

        if (Controls.Press(Keys.F11))
        {
            Settings.Fullscreen = !Settings.Fullscreen;
            ApplyDisplay();
            SaveSettings();
        }

        if (Controls.Press(Keys.F3))
        {
            showDiagnostics = !showDiagnostics;
            diagnosticWorld = null;
        }

        if (Controls.Press(Keys.F4))
        {
            Renderer.ShowColliders = !Renderer.ShowColliders;
        }
    }

    private void CheckInvitations()
    {
        ulong invitation = Online.PollInvites();
        if (invitation == 0)
        {
            return;
        }

        if (Online.Lobby?.Connected == true)
            Lobby.RememberParty();
        Match.Close();
        Online.CloseLobby();
        BeginLobby("steam:" + invitation, false);
    }

    internal void AdvanceMatch(double elapsedSeconds)
    {
        Match.Update(elapsedSeconds, TickLimit);
        if (Match.Error != null)
        {
            Fail(Match.Error);
        }
    }

    internal bool TerminalConfirmed() => Match.TerminalConfirmed;

    internal void StartLocal()
    {
        LastError = null;
        var options = Setup.CreateOptions(Options.MapOrder);
        if (options.Rules.TeamMode && options.Rules.Teams.Take(Setup.Seats.Count).Distinct().Count() < 2)
        {
            Menus.ShowSeats();
            Toasts.Show("CHOOSE TWO TEAMS");
            return;
        }

        Match.StartLocal(options, Setup.Seats);
        ShowMatch();
    }

    internal void BeginLobby(string target, bool host)
    {
        if (
            !host
            && target.StartsWith("steam:", StringComparison.Ordinal)
            && (!ulong.TryParse(target[6..], out ulong id) || id == 0)
        )
        {
            Toasts.Show("ENTER A VALID LOBBY ID");
            return;
        }
        if (
            !host
            && target.StartsWith("udp:", StringComparison.Ordinal)
            && !LobbyAddress.TryUdp(target[4..], Options.Port, out _, out _)
        )
        {
            Toasts.Show("ENTER A VALID ADDRESS");
            return;
        }
        connectionFromMainMenu = Menus.ShowingMenuBackground || Menus.ShowingCinematic;
        LastError = null;
        try
        {
            if (Online.Lobby?.Connected == true)
                Lobby.RememberParty();
            connectionParty = Setup.Lobby.Roster.Players(0);
            Online.Connect(
                target,
                host,
                Setup.Lobby.Roster.Capacity,
                Setup.Lobby.Roster,
                Setup.CreateOptions(Options.MapOrder),
                Menus.AllowLan,
                Menus.HintDevice,
                Menus.Creation.Privacy,
                Settings.SteamTransport
            );
            Lobby.Open();
            Menus.ShowConnecting();
            Toasts.Show(host ? "OPENING LOBBY" : "CONNECTING");
        }
        catch (Exception exception)
        {
            Fail(exception.Message, host ? "CANNOT HOST LOBBY" : "CANNOT JOIN LOBBY");
        }
    }

    internal void StartNetwork()
    {
        try
        {
            Match.StartNetwork(Online.Lobby!);
            matchGeneration = Online.Lobby!.Generation;
            Console.WriteLine(
                $"Connected peer {Online.Lobby!.LocalPeer}; players {Match.World!.Players.Length}; initial hash {Match.World.HashState():x16}"
            );
            ShowMatch();
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private void ShowMatch()
    {
        Cinematics.Stop();
        Audio.StopTitleMusic();
        Menus.ShowPlaying();
    }

    internal void ReturnToLobby()
    {
        if (Online.Lobby is { } online)
        {
            if (!online.IsHost)
            {
                Toasts.Show("HOST ONLY");
                return;
            }
            if (!online.ReturnToLobby())
                return;
        }
        OpenCurrentLobby();
    }

    private void CheckLobbyReturn()
    {
        if (Match.Network == null || Online.Lobby is not { } online)
            return;
        if (online.Generation != matchGeneration)
        {
            OpenCurrentLobby();
            Toasts.Show("BACK IN LOBBY");
        }
    }

    private void OpenCurrentLobby()
    {
        Match.Close();
        Cinematics.Stop();
        Lobby.Open();
        Menus.ShowSeats();
    }

    internal void CancelConnection()
    {
        if (connectionFromMainMenu)
        {
            MainMenu();
            return;
        }
        if (Online.Lobby?.Connected == true)
            Lobby.RememberParty();
        else
        {
            Setup.Lobby.Roster.Reset();
            Setup.Lobby.Roster.SetPlayers(0, connectionParty);
        }
        Match.Close();
        Online.CloseLobby();
        Cinematics.Stop();
        Lobby.Open();
        Menus.ShowSeats();
    }

    internal void WatchCpus()
    {
        var options = Setup.CreateOptions(Options.MapOrder);
        options.Rules.PlayerCount = 4;
        options.Rules.Teams = [0, 1, 0, 1, 0, 1, 0, 1];
        Match.StartLocal(options, Enumerable.Range(0, 4).Select(i => new LocalSeat(-1, i % 2)).ToArray());
        ShowMatch();
    }

    internal void MainMenu()
    {
        LastError = null;
        connectionParty = [];
        Cinematics.Stop();
        Online.CloseLobby();
        Lobby.Close();
        Match.StartMenuBackground(Options.Seed);
        Menus.ShowMain();
    }

    internal void Fail(string message, string? toast = null)
    {
        CancelConnection();
        LastError = message;
        Toasts.Show(toast ?? UserMessages.ConnectionError(message));
        Console.Error.WriteLine(message);
        if (Options.Host != null || Options.Join != null)
        {
            Environment.ExitCode = 1;
        }
    }

    internal void ApplyDisplay()
    {
        Controls.SuspendMouseHover();
        display.Apply();
    }

    internal void SaveSettings()
    {
        Online.Lobby?.SetMatchSettings(
            System.Text.Json.JsonSerializer.Serialize(Setup.CreateOptions(Options.MapOrder))
        );
        ScheduleSettingsSave();
        settingsSave.Flush();
    }

    internal void ScheduleSettingsSave() => settingsSave.Request();

    private void WriteSettings()
    {
        if (Options.LocalTestCount > 0)
            return;
        try
        {
            Settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Settings: " + exception.Message);
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        DrawScene();
        Renderer.BeginUi();
        menuRenderer.Draw();
        timing.RecordFrame(gameTime.ElapsedGameTime.TotalSeconds);
        var diagnosticScene =
            Menus.ShowingLobby ? Lobby.World
            : Match.IsMenuBackground ? null
            : Match.World;
        if (showDiagnostics && diagnosticScene != null)
        {
            DrawDiagnostics(diagnosticScene, gameTime.ElapsedGameTime.TotalSeconds);
        }
        Connections.Draw();
        Connections.DrawInputWait();
        toastRenderer.Draw();
        Renderer.EndUi();
        Renderer.Present();
        RenderedFrames++;
        int? frameLimit = LimitFrameRate && (!Settings.VSync || Options.Offscreen) ? Settings.FrameLimit : null;
        timing.FinishFrame(frameLimit);
        base.Draw(gameTime);
    }

    private void DrawScene()
    {
        if (Menus.ShowingCinematic)
        {
            Renderer.DrawPresentation(Cinematics.Draw());
        }
        else if (Menus.ShowingLobby && Lobby.World != null)
        {
            Renderer.DrawWorld(Lobby.World, Lobby.PreviousWorld, Lobby.Interpolation, showGameplayUi: false);
            Audio.ListenerPosition = Renderer.ListenerPosition;
        }
        else if (Match.World != null && (Menus.ShowingMatch || Menus.ShowingMenuBackground))
        {
            var world = Match.World;
            if (world.Phase == MatchPhase.RoundScores)
            {
                Renderer.DrawRoundScores(
                    world,
                    (world.Rules.ScoreScreenTicks - world.PhaseTicks) / (float)World.TickRate
                );
            }
            else
            {
                Renderer.DrawWorld(
                    world,
                    Match.PreviousWorld,
                    Match.Interpolation,
                    showGameplayUi: !Match.IsMenuBackground
                );
            }

            Audio.ListenerPosition = Renderer.ListenerPosition;
        }
        else
        {
            Renderer.DrawBackdrop(Assets.Data.Maps[Setup.FirstMap], .12f);
        }
    }

    private void DrawDiagnostics(World world, double elapsedSeconds)
    {
        if (diagnosticWorld != world || world.TickNumber < diagnosticTick)
        {
            diagnosticWorld = world;
            diagnosticTick = world.TickNumber;
            diagnosticSeconds = 0;
            diagnosticTickRate = 0;
        }
        diagnosticSeconds += elapsedSeconds;
        if (diagnosticSeconds >= 0.5)
        {
            diagnosticTickRate = (world.TickNumber - diagnosticTick) / diagnosticSeconds;
            diagnosticTick = world.TickNumber;
            diagnosticSeconds = 0;
        }
        var network = Menus.ShowingLobby ? Online.Lobby?.LobbySession : Match.Network;
        var lines = new List<string>
        {
            $"{timing.FramesPerSecond:F0} FPS | {diagnosticTickRate:F1}/{World.TickRate} HZ | TICK {world.TickNumber}",
        };
        if (network != null)
        {
            string status =
                Menus.ShowingLobby && Online.Lobby!.Transitioning ? Online.Lobby.Status : network.WaitReason;
            lines.Add($"PEER {network.LocalPeer} | {network.State} | {status}");
            lines.Add(
                $"CONF {network.ConfirmedFrame} | PRED {network.PredictionDepth} | AHEAD {network.FramesAheadOfPeers}"
            );
            lines.Add(
                $"ROLLBACKS {network.RollbackCount} | REPLAYED {network.ResimulatedTicks} | BUFFER {network.BufferedFrames}"
            );
            lines.Add($"PACE {network.FrameDurationMultiplier:F2} | REJECTED {network.RejectedPackets}");
            foreach (var peer in network.PeerStats)
                lines.Add(
                    $"P{peer.PeerId} {peer.State} | RTT {peer.RoundTripMilliseconds:F0} MS | PENDING {peer.PendingInputFrames} | AHEAD {peer.FramesAhead:F1}"
                );
        }
        else if (Menus.ShowingLobby && Online.Lobby != null)
            lines.Add(Online.Lobby.Status);
        Renderer.Panel(new Rectangle(18, 52, 790, 20 + lines.Count * 27), new Color(14, 23, 29, 236));
        for (int row = 0; row < lines.Count; row++)
            Renderer.Text(lines[row], 30, 62 + row * 27, row == 0 ? new Color(255, 211, 86) : Color.White);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        Match?.Network?.Dispose();
        Online?.CloseLobby();
        Match?.SaveReplay();
        if (contentLoaded && SaveSettingsOnExit)
        {
            SaveSettings();
        }

        base.OnExiting(sender, args);
        Shutdown?.Complete();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Match?.Dispose();
            Online?.Dispose();
            Cinematics?.Dispose();
            Audio?.Dispose();
            Renderer?.Dispose();
            Assets?.Dispose();
            windowIcons?.Dispose();
        }

        base.Dispose(disposing);
    }
}
