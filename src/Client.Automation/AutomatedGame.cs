using System.Text.Json;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.Automation;

internal sealed class AutomatedGame : FrogGame
{
    private readonly AutomationOptions automation;
    private double virtualClock;
    private bool requestedSpectating;
    private bool resultWritten;
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
        if (automation.Spectate && !requestedSpectating && Online.Lobby is { Connected: true, Starting: false } lobby)
            requestedSpectating = lobby.SetSpectating(lobby.LocalPeer, true);
        if (
            automation.StartPlayers > 0
            && Online.Lobby is { IsHost: true, Starting: false } online
            && online.Roster.Count == automation.StartPlayers
            && online.Roster.Spectators.Count >= automation.StartSpectators
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
        if (!framesComplete && !ticksComplete && LastError == null)
        {
            return;
        }

        if (ticksComplete && Match.Network != null && LastError == null)
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

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        WriteResult();
        base.OnExiting(sender, args);
    }

    private void WriteResult()
    {
        if (resultWritten)
            return;
        resultWritten = true;
        Match.SaveReplay();
        if (automation.Result == null)
        {
            return;
        }

        var buffer = GraphicsDevice.PresentationParameters;
        var playerActions = Menus.Screen == GameScreen.ViewPlayers ? Menus.PlayerActions() : default;
        var result = new
        {
            Page = Menus.Screen.ToString(),
            Menus.HintDevice,
            Menus.Selected,
            Menus.SelectedSeat,
            MenuItems = Menus.Entries().Select(entry => entry.Text).ToArray(),
            ConnectionsVisible = Connections.Visible,
            InputWaitMessage = Connections.InputWaitMessage(),
            ConnectionPlayers = Connections.Visible ? Connections.Rows() : null,
            PlayerActions = new
            {
                Accept = playerActions.Accept?.Label,
                Remove = playerActions.Remove?.Label,
                DisabledReason = playerActions.Accept?.DisabledReason ?? playerActions.Remove?.DisabledReason,
            },
            LocalDevices = Setup.Seats.Select(seat => seat.Device).ToArray(),
            LobbySlots = Menus.ShowingLobby ? Lobby.Roster.Slots : null,
            PresentedLobbySlots = Menus.ShowingLobby ? Lobby.Presentation.Roster.Slots : null,
            LobbyRoomStatus = Menus.ShowingLobby
                ? Enumerable.Range(0, 8).Select(Lobby.Presentation.Status).ToArray()
                : null,
            LobbyProgress = Menus.ShowingLobby ? Lobby.ProgressText : null,
            LobbySlotTypes = Menus.ShowingLobby ? Enumerable.Range(0, 8).Select(Lobby.GetSlotType).ToArray() : null,
            PendingSlotEdits = Menus.ShowingLobby
                ? Enumerable.Range(0, 8).Where(Lobby.SlotEditPending).ToArray()
                : null,
            LobbySpectators = Online.Lobby?.Roster.Spectators,
            JoinHintDevices = Menus.ShowingLobby ? Lobby.JoinHintDevices() : null,
            MatchPeerSlots = Online.Lobby?.PeerSlots,
            LobbyPlayers = Menus.ShowingLobby
                ? Lobby
                    .World?.Players.Select(
                        (player, room) =>
                            new
                            {
                                player.Alive,
                                player.ColorIndex,
                                player.OnGround,
                                CanChooseAgain = Lobby.CanChooseAgain(room),
                                X = player.X.ToFloat(),
                                Y = player.Y.ToFloat(),
                            }
                    )
                    .ToArray()
                : null,
            LobbySpawnPuffs = Menus.ShowingLobby
                ? Renderer.Effects.Active.Count(effect => effect.Name == "SpawnPuff")
                : 0,
            LobbyTick = Menus.ShowingLobby ? Lobby.World?.TickNumber : null,
            MatchSettings = Setup.Preferences,
            Settings.Volume,
            MenuBackground = Match.IsMenuBackground,
            Paused = Menus.LocalPresentationPaused,
            Fullscreen,
            HardwareModeSwitch,
            Window.AllowUserResizing,
            WindowWidth = Window.ClientBounds.Width,
            WindowHeight = Window.ClientBounds.Height,
            BackBufferWidth = buffer.BackBufferWidth,
            BackBufferHeight = buffer.BackBufferHeight,
            TickNumber = Match.World?.TickNumber,
            TickRate = World.TickRate,
            Hash = Match.World?.HashState().ToString("x16"),
            Map = Match.World?.Map.Id,
            RoundNumber = Match.World?.RoundNumber,
            Phase = Match.World?.Phase.ToString(),
            Players = Match.World?.Players.Length,
            RenderedFrames,
            RollbackCount = Match.Network?.RollbackCount ?? 0,
            ConfirmedFrame = Match.Network?.ConfirmedFrame,
            Error = LastError ?? Match.Network?.Error,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(automation.Result))!);
        File.WriteAllText(
            automation.Result,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
        );
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
