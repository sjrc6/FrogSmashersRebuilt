using System.Text.Json;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.Automation;

internal sealed class AutomatedGame : FrogGame
{
    private readonly AutomationOptions automation;
    private double virtualClock;
    private long? networkFinishMilliseconds;
    protected override bool LimitFrameRate => automation.Frames == 0;
    protected override bool SaveSettingsOnExit => false;
    protected override long TickLimit => automation.Ticks > 0 ? automation.Ticks : long.MaxValue;

    public AutomatedGame(AutomationOptions automation)
        : base(automation.Game)
    {
        this.automation = automation;
        if (automation.InputScript != null)
        {
            var input = new ScriptedInput(automation.InputScript);
            Controls.KeyboardSource = () => input.State(RenderedFrames);
            Controls.GamePadSource = pad => input.Pad(RenderedFrames, pad);
            Controls.MouseSource = () => input.Mouse(RenderedFrames);
        }
    }

    protected override double ElapsedSeconds(GameTime gameTime)
    {
        if (automation.Frames == 0 || Options.Host != null || Options.Join != null)
        {
            return base.ElapsedSeconds(gameTime);
        }

        double target = (RenderedFrames + 1.0) / automation.RenderFps;
        double elapsed = Math.Max(0, target - virtualClock);
        virtualClock = target;
        return elapsed;
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (
            automation.StartPlayers > 0
            && Online.Lobby is { IsHost: true, Starting: false } online
            && online.Roster.Count == automation.StartPlayers
        )
            online.StartMatch(JsonSerializer.Serialize(Setup.CreateOptions(Options.MapOrder)));
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        bool framesComplete = automation.Frames > 0 && RenderedFrames >= automation.Frames;
        bool ticksComplete =
            automation.Ticks > 0
            && !Match.IsMenuBackground
            && Match.World != null
            && (Match.World.TickNumber >= automation.Ticks || Match.World.Phase == MatchPhase.MatchFinished)
            && Match.TerminalConfirmed;
        if (!framesComplete && !ticksComplete && Menus.Screen != GameScreen.Error)
        {
            return;
        }

        if (ticksComplete && Match.Network != null && Menus.Screen != GameScreen.Error)
        {
            // Artificial tick limits have no outro. Keep polling briefly so the other process receives our final hash.
            networkFinishMilliseconds ??= Environment.TickCount64;
            if (Environment.TickCount64 - networkFinishMilliseconds.Value < 500)
                return;
        }

        if (automation.Capture != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(automation.Capture))!);
            using var capture = File.Create(automation.Capture);
            Renderer.Frame.SaveAsPng(capture, Renderer.Frame.Width, Renderer.Frame.Height);
        }

        WriteResult();
        Exit();
    }

    private void WriteResult()
    {
        Match.SaveReplay();
        if (automation.Result == null)
        {
            return;
        }

        var buffer = GraphicsDevice.PresentationParameters;
        var result = new
        {
            Page = Menus.Screen.ToString(),
            Menus.Owner,
            Menus.Selected,
            Menus.SelectedSeat,
            MenuItems = Menus.Entries().Select(entry => entry.Label).ToArray(),
            LocalDevices = Setup.Seats.Select(seat => seat.Device).ToArray(),
            LobbySlots = Menus.ShowingLobby ? Lobby.Roster.Slots : null,
            LobbyPlayers = Menus.ShowingLobby
                ? Lobby
                    .World?.Players.Select(player => new
                    {
                        player.Alive,
                        player.ColorIndex,
                        X = player.X.ToFloat(),
                        Y = player.Y.ToFloat(),
                    })
                    .ToArray()
                : null,
            LobbySpawnPuffs = Menus.ShowingLobby
                ? Renderer.Effects.Active.Count(effect => effect.Name == "SpawnPuff")
                : 0,
            LobbyTick = Menus.ShowingLobby ? Lobby.World?.TickNumber : null,
            MatchDefaults = Setup.Preferences,
            Settings.Volume,
            MenuBackground = Match.IsMenuBackground,
            Paused = Menus.LocalPresentationPaused,
            Fullscreen,
            HardwareModeSwitch,
            Settings.FontSmoothing,
            Renderer.TextEdgeWidth,
            WindowWidth = Window.ClientBounds.Width,
            WindowHeight = Window.ClientBounds.Height,
            BackBufferWidth = buffer.BackBufferWidth,
            BackBufferHeight = buffer.BackBufferHeight,
            TickNumber = Match.World?.TickNumber,
            Hash = Match.World?.HashState().ToString("x16"),
            Map = Match.World?.Map.Id,
            RoundNumber = Match.World?.RoundNumber,
            Phase = Match.World?.Phase.ToString(),
            Players = Match.World?.Players.Length,
            RenderedFrames,
            RollbackCount = Match.Network?.RollbackCount ?? 0,
            ConfirmedFrame = Match.Network?.ConfirmedFrame,
            Error = Menus.Screen == GameScreen.Error ? Menus.Status : Match.Network?.Error,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(automation.Result))!);
        File.WriteAllText(
            automation.Result,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
