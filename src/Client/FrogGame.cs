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
    private int matchGeneration;
    private bool showDiagnostics;
    private bool contentLoaded;
    internal LaunchOptions Options { get; }
    internal ClientSettings Settings { get; } = ClientSettings.Load();
    internal Controls Controls { get; }
    internal Assets Assets { get; private set; } = null!;
    internal Renderer Renderer { get; private set; } = null!;
    internal Audio Audio { get; private set; } = null!;
    internal CinematicPlayer Cinematics { get; private set; } = null!;
    internal MatchSetup Setup { get; }
    internal LobbyController Lobby { get; private set; } = null!;
    internal MatchController Match { get; private set; } = null!;
    internal OnlineController Online { get; private set; } = null!;
    internal MenuController Menus { get; private set; } = null!;
    internal ToastController Toasts { get; } = new();
    internal string? LastError { get; private set; }
    internal int RenderedFrames { get; private set; }
    internal bool Fullscreen => graphics.IsFullScreen;
    internal bool HardwareModeSwitch => graphics.HardwareModeSwitch;
    protected virtual bool LimitFrameRate => true;
    protected virtual bool SaveSettingsOnExit => true;
    protected virtual long TickLimit => long.MaxValue;

    public FrogGame(LaunchOptions options)
    {
        Options = options;
        Controls = new Controls(Settings);
        Setup = new MatchSetup(options.Seed);
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Renderer.Width,
            PreferredBackBufferHeight = Renderer.Height,
            SynchronizeWithVerticalRetrace = !options.Offscreen && Settings.VSync,
            IsFullScreen = !options.Offscreen && Settings.Fullscreen,
            HardwareModeSwitch = false,
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        display = new DisplaySettings(Window, graphics, Settings, options.Offscreen);
        Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
        IsMouseVisible = true;
        Window.AllowUserResizing = false;
        Window.Title = "Frog Smashers Rebuilt";
        Window.TextInput += (_, input) => Menus?.EnterText(input.Character);
        Window.ClientSizeChanged += (_, _) => Controls.SuspendMouseHover();
    }

    protected override void LoadContent()
    {
        display.MatchDesktopBackBuffer();
        if (OperatingSystem.IsWindows())
            windowIcons = new WindowsWindowIcons(Window.Handle);
        Assets = new Assets(Content, Content.RootDirectory);
        Renderer = new Renderer(GraphicsDevice, Assets) { ShakeEnabled = Settings.ScreenShake };
        Audio = new Audio(Assets, !Options.NoAudio) { Volume = Settings.Volume };
        Cinematics = new CinematicPlayer(GraphicsDevice, Assets, Audio) { ShakeEnabled = Settings.ScreenShake };
        Match = new MatchController(Assets.Data, Renderer, Audio, Controls, Options.Record);
        Online = new OnlineController(Options, Assets.Data.ContentHash);
        Lobby = new LobbyController(this);
        Menus = new MenuController(this);
        menuRenderer = new MenuRenderer(this, Menus);
        toastRenderer = new ToastRenderer(this);
        ConfigureLaunch();
        contentLoaded = true;
    }

    private void ConfigureLaunch()
    {
        if (Options.Map != null)
        {
            Setup.FirstMap = int.TryParse(Options.Map, out int index)
                ? index
                : Assets.Data.Maps.FindIndex(map => map.Id.Equals(Options.Map, StringComparison.OrdinalIgnoreCase));
            if (Setup.FirstMap < 0 || Setup.FirstMap >= Assets.Data.Maps.Count)
            {
                throw new ArgumentException("Unknown map: " + Options.Map);
            }
        }

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
        Controls.Poll(IsActive || Options.Offscreen);
        double elapsedSeconds = ElapsedSeconds(gameTime);
        Toasts.Update(elapsedSeconds);
        CheckLobbyReturn();
        HandleGlobalInput();
        Renderer.Update(Menus.LocalPresentationPaused ? 0 : (float)elapsedSeconds);
        Audio.SetPaused(Menus.LocalPresentationPaused);
        Audio.Update();
        if (Menus.ShowingCinematic)
        {
            Cinematics.Update((float)elapsedSeconds);
        }

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
        if (!host && target.StartsWith("udp:", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(target[4..]))
        {
            Toasts.Show("ENTER AN ADDRESS");
            return;
        }
        LastError = null;
        try
        {
            if (Online.Lobby?.Connected == true)
                Lobby.RememberParty();
            Online.Connect(
                target,
                host,
                Setup.Lobby.Roster.Capacity,
                Setup.Lobby.Roster,
                Setup.CreateOptions(Options.MapOrder),
                Menus.AllowLan,
                Menus.HintDevice
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
        online.Poll();
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

    internal void LeaveOnlineLobby()
    {
        Lobby.RememberParty();
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
        Cinematics.Stop();
        Online.CloseLobby();
        Lobby.Close();
        Match.StartMenuBackground(Options.Seed);
        Menus.ShowMain();
    }

    internal void Fail(string message, string? toast = null)
    {
        LeaveOnlineLobby();
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
        if (showDiagnostics && Match.World != null && !Match.IsMenuBackground)
        {
            DrawDiagnostics();
        }
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

    private void DrawDiagnostics()
    {
        var world = Match.World!;
        Renderer.Panel(new Rectangle(18, 52, 520, 98), new Color(14, 23, 29, 236));
        Renderer.Text(
            $"{timing.FramesPerSecond:F0} FPS | 120 HZ | TICK {world.TickNumber}",
            30,
            62,
            new Color(255, 211, 86),
            1
        );
        Renderer.Text($"HASH {world.HashState():x16}", 30, 89, Color.White);
        if (Match.Network != null)
        {
            var network = Match.Network;
            Renderer.Text(
                $"PRED {network.PredictionDepth} | ROLLBACKS {network.RollbackCount} | CONF {network.ConfirmedFrame}",
                30,
                116,
                new Color(173, 190, 200),
                1
            );
        }
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        Match?.SaveReplay();
        if (contentLoaded && SaveSettingsOnExit)
        {
            SaveSettings();
        }

        base.OnExiting(sender, args);
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
