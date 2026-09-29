using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public class FrogGame : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly DisplaySettings display;
    private readonly FrameTiming timing = new();
    private MenuRenderer menuRenderer = null!;
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
    internal MatchController Match { get; private set; } = null!;
    internal OnlineController Online { get; private set; } = null!;
    internal MenuController Menus { get; private set; } = null!;
    internal float FontEdgeWidth =>
        Settings.FontSmoothing switch
        {
            0 => 0,
            1 => 0.5f,
            _ => 1,
        };
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
        Setup = new MatchSetup(Settings, options.Seed);
        if (options.Width > 0)
        {
            Settings.Width = options.Width;
        }

        if (options.Height > 0)
        {
            Settings.Height = options.Height;
        }

        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Settings.Width,
            PreferredBackBufferHeight = Settings.Height,
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
        Window.AllowUserResizing = true;
        Window.Title = "Frog Smashers Rebuilt";
        Window.TextInput += (_, input) => Menus?.EnterText(input.Character);
    }

    protected override void LoadContent()
    {
        display.MatchDesktopBackBuffer();
        Assets = new Assets(Content, Content.RootDirectory);
        Renderer = new Renderer(GraphicsDevice, Assets)
        {
            ShakeEnabled = Settings.ScreenShake,
            TextEdgeWidth = FontEdgeWidth,
        };
        Audio = new Audio(Assets, !Options.NoAudio) { Volume = Settings.Volume };
        Cinematics = new CinematicPlayer(GraphicsDevice, Assets, Audio) { ShakeEnabled = Settings.ScreenShake };
        Match = new MatchController(Assets.Data, Renderer, Audio, Controls, Options.Record);
        Online = new OnlineController(Options, Assets.Data.ContentHash);
        Menus = new MenuController(this);
        menuRenderer = new MenuRenderer(this, Menus);
        ConfigureLaunch();
        contentLoaded = true;
    }

    private void ConfigureLaunch()
    {
        if (Options.MapOrder != null)
        {
            Setup.Rules.MapOrder = Options.MapOrder;
        }

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
                Setup.Seats.Add(new LocalSeat(Options.Demo ? -1 : index, -1));
            }

            BeginLobby(Options.Host ?? Options.Join!, Options.Host != null);
        }
        else if (Options.Replay != null)
        {
            Setup.Options = Match.StartReplay(Options.Replay);
            ShowMatch();
        }
        else if (Options.Demo)
        {
            for (int index = 0; index < Options.Players; index++)
            {
                Setup.Seats.Add(new LocalSeat(-1, index % 2));
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
        Controls.Poll();
        double elapsedSeconds = ElapsedSeconds(gameTime);
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

        bool togglePause =
            Controls.Press(Keys.Escape) || Controls.AnyPad(Buttons.Back) || Controls.AnyPad(Buttons.Start);
        if (Menus.Screen == GameScreen.Playing && togglePause)
        {
            Match.Paused = !Match.Paused;
            Controls.ClearPendingEdges();
        }
    }

    private void CheckInvitations()
    {
        ulong invitation = Online.PollInvites();
        if (invitation == 0)
        {
            return;
        }

        Match.Close();
        Online.CloseLobby();
        if (Setup.Seats.Count == 0)
        {
            Setup.Seats.Add(new LocalSeat(0, -1));
        }

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
        Setup.ConfigureRules(Options.MapOrder);
        if (Setup.Rules.TeamMode && Setup.Seats.Select(seat => seat.Team).Distinct().Count() < 2)
        {
            Menus.Status = "CHOOSE AT LEAST TWO DIFFERENT TEAMS";
            Menus.Screen = GameScreen.Seats;
            return;
        }

        Match.StartLocal(Setup.Options, Setup.Seats);
        ShowMatch();
    }

    internal void BeginLobby(string target, bool host)
    {
        try
        {
            Setup.ConfigureRules(Options.MapOrder);
            Online.Connect(target, host, Menus.ExpectedPeers, Setup);
            Menus.Screen = GameScreen.Connecting;
            Menus.EditingAddress = false;
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    internal void StartNetwork()
    {
        try
        {
            Setup.Options = Match.StartNetwork(Online.Lobby!, Setup.Seats);
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
        Menus.Screen = GameScreen.Playing;
    }

    internal void MainMenu()
    {
        Cinematics.Stop();
        Online.CloseLobby();
        Match.StartMenuBackground(Options.Seed);
        Menus.ShowMain();
    }

    internal void Fail(string message)
    {
        Match.Close();
        Online.CloseLobby();
        Menus.Status = message;
        Menus.Screen = GameScreen.Error;
        Cinematics.Stop();
        Console.Error.WriteLine(message);
        if (Options.Host != null || Options.Join != null)
        {
            Environment.ExitCode = 1;
        }
    }

    internal void ApplyDisplay() => display.Apply();

    internal void SaveSettings()
    {
        Settings.Rules = Setup.Rules;
        Settings.FirstMap = Setup.FirstMap;
        Settings.ShuffleMaps = Setup.ShuffleMaps;
        Settings.ExpectedPeers = Menus.ExpectedPeers;
        display.RememberWindowSize();
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
            Renderer.DrawBackdrop(Assets.Data.Maps[Setup.FirstMap]);
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
            0.85f
        );
        Renderer.Text($"HASH {world.HashState():x16}", 30, 89, Color.White, 0.85f);
        if (Match.Network != null)
        {
            var network = Match.Network;
            Renderer.Text(
                $"PRED {network.PredictionDepth} | ROLLBACKS {network.RollbackCount} | CONF {network.ConfirmedFrame}",
                30,
                116,
                new Color(173, 190, 200),
                0.8f
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
        }

        base.Dispose(disposing);
    }
}
