using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private static readonly int[] FrameRates = [60, 90, 120, 144, 165, 240, 360, 500];
    private readonly FrogGame game;
    private readonly Stack<(GameScreen Screen, int Selected)> history = new();
    private GameScreen context = GameScreen.Main;
    private bool menuSoundPending;
    private readonly MenuAdjustRepeat adjustRepeat = new();
    private (GameScreen Screen, int Row) repeatTarget;
    public LobbyCreation Creation { get; } = new();
    public double AnimationTime { get; private set; }
    public GameScreen Screen { get; set; } = GameScreen.Intro;
    public int Selected { get; set; }
    public int SelectedSeat { get; private set; }
    public int HintDevice { get; private set; }
    public int BindingDevice { get; private set; }
    public bool AllowLan { get; private set; }
    public bool ShowingLobby => context == GameScreen.Seats;
    public bool EditingAddress { get; set; }
    public bool WaitingForBinding { get; private set; }
    public string JoinAddress { get; private set; } = "127.0.0.1";
    public bool ShowingCinematic => Screen is GameScreen.Intro or GameScreen.Title or GameScreen.Outro;
    public bool ShowingMenuBackground => !ShowingCinematic && context == GameScreen.Main;
    public bool ShowingMatch => !ShowingCinematic && context == GameScreen.Playing;
    public bool LocalPresentationPaused => ShowingMatch && game.Match.Network == null && game.Match.Paused;
    private MatchPreferences Rules => game.Setup.Preferences;

    public MenuController(FrogGame game)
    {
        this.game = game;
        AllowLan = game.Options.Lan;
    }

    public void EnterText(char character)
    {
        if (!EditingAddress || char.IsControl(character))
            return;
        if (
            Screen == GameScreen.JoinUdp
            && (char.IsAsciiLetterOrDigit(character) || ".:-_".Contains(character))
            && JoinAddress.Length < 128
        )
        {
            JoinAddress += character;
            menuSoundPending = true;
        }
    }

    public void Update(double elapsedSeconds)
    {
        AnimationTime += elapsedSeconds;
        if (Screen is GameScreen.BrowseSteam or GameScreen.BrowseLan)
            UpdateBrowser();
        if (Screen == GameScreen.Bindings)
            RefreshBindingDevice();
        var input = MenuInput.Read(game.Controls);
        input = RepeatAdjustment(input, elapsedSeconds);
        if (
            game.Controls.KeysNow.GetPressedKeys().Any(game.Controls.Press)
            || game.Controls.MouseMoved
            || game.Controls.MousePressed
            || game.Controls.MouseRightPressed
        )
            HintDevice = 0;
        else
            for (int device = 2; device < 10; device++)
                if (MenuInput.Read(game.Controls, device) != default)
                    HintDevice = device;
        switch (Screen)
        {
            case GameScreen.Intro:
                if (input.Accept || input.Back || game.Controls.Press(Keys.Space) || game.Cinematics.TitleReady)
                {
                    game.Cinematics.SkipIntro();
                    Screen = GameScreen.Title;
                    menuSoundPending = input.Accept || input.Back || game.Controls.Press(Keys.Space);
                }
                break;
            case GameScreen.Title:
                if (input.Accept || game.Controls.Press(Keys.Space))
                {
                    game.MainMenu();
                    menuSoundPending = true;
                }
                break;
            case GameScreen.Seats:
                UpdateSeats();
                break;
            case GameScreen.Playing:
                if (!game.Match.Paused)
                {
                    if (game.Controls.MenuDevice() is int device)
                    {
                        HintDevice = device;
                        game.Match.Paused = true;
                        Selected = 0;
                        game.Controls.ClearPendingEdges();
                        menuSoundPending = true;
                    }
                }
                else
                    UpdateRows(input);
                break;
            case GameScreen.Connecting:
                UpdateConnecting(input);
                break;
            case GameScreen.SlotEditor:
                UpdateRoomSelection(input);
                break;
            case GameScreen.ViewPlayers:
                UpdatePlayerList(input);
                break;
            case GameScreen.Outro:
                if (game.Match.Network?.Error != null && !game.Match.Network.IsTransportFailure)
                    game.Fail(game.Match.Network.Error);
                else if (game.Online.Lobby is { IsHost: false })
                {
                    if (input.Back)
                        game.MainMenu();
                }
                else if (input.Back || input.Accept || game.Cinematics.Finished)
                {
                    game.ReturnToLobby();
                    menuSoundPending = input.Back || input.Accept;
                }
                break;
            case GameScreen.Bindings when WaitingForBinding:
                CaptureBinding();
                break;
            case GameScreen.JoinUdp when EditingAddress:
                UpdateAddress(input);
                break;
            default:
                UpdateRows(input);
                break;
        }

        if (ShowingLobby)
        {
            game.Lobby.Update(elapsedSeconds);
        }

        if (ShowingMatch)
        {
            game.AdvanceMatch(elapsedSeconds);
            if (
                Screen == GameScreen.Playing
                && !game.Match.Paused
                && game.Match.World?.Phase == MatchPhase.MatchFinished
                && !game.Options.Demo
                && !game.Match.ReplayPlayback
                && game.TerminalConfirmed()
            )
            {
                game.Match.SaveReplay();
                game.Cinematics.StartOutro(
                    game.Match.World.Winner >= 0
                        ? game.Renderer.ColorFor(game.Match.World, game.Match.World.Winner)
                        : Color.White
                );
                Screen = GameScreen.Outro;
                game.Audio.Reset();
            }
        }

        if (menuSoundPending)
        {
            menuSoundPending = false;
            game.Audio.PlayMenuAction();
        }
    }

    private MenuInput RepeatAdjustment(MenuInput input, double elapsedSeconds)
    {
        var target = (Screen, Selected);
        if (repeatTarget != target)
            adjustRepeat.Reset();
        repeatTarget = target;
        var entry = Entries().ElementAtOrDefault(Selected);
        if (
            input.Vertical != 0
            || game.Controls.MouseMoved
            || entry is not { RepeatAdjust: true, DisabledReason: null }
        )
        {
            adjustRepeat.Reset();
            return input;
        }
        int held =
            input.HorizontalHeld != 0 ? input.HorizontalHeld
            : input.AcceptHeld ? 1
            : 0;
        int pressed =
            input.Horizontal != 0 ? input.Horizontal
            : input.Accept ? 1
            : 0;
        int adjustment = adjustRepeat.Read(held, pressed, elapsedSeconds);
        return input with { Horizontal = adjustment, Accept = false };
    }

    private void UpdateRows(MenuInput input)
    {
        var entries = Entries();
        var layout = MeasurePointer(entries);
        if (input.Back || ClickBackHint(layout))
        {
            Back();
            return;
        }
        if (entries.Count == 0)
            return;
        SelectRow(Wrap(Math.Min(Selected, entries.Count - 1) + input.Vertical, entries.Count));
        if (ClickRow(entries, layout))
            return;
        if (input.Horizontal != 0)
        {
            if (entries[Selected].Change != null)
            {
                ChangeEntry(entries[Selected], input.Horizontal);
            }
        }
        else if (input.Accept)
            ActivateEntry(entries[Selected]);
    }

    private void SelectRow(int index)
    {
        menuSoundPending |= Selected != index;
        Selected = index;
    }

    private void ActivateEntry(MenuEntry entry)
    {
        if (entry.DisabledReason is { } reason)
            game.Toasts.Show(reason);
        else
            entry.Select();
        menuSoundPending = true;
    }

    private void ChangeEntry(MenuEntry entry, int amount)
    {
        if (entry.DisabledReason is { } reason)
            game.Toasts.Show(reason);
        else
            entry.Change?.Invoke(amount);
        menuSoundPending = true;
    }

    private bool ClickRow(IReadOnlyList<MenuEntry> entries, MenuLayout.Measurement? layout)
    {
        if (layout == null || Pointer() is not Point point)
            return false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (!layout.Rows[i].Bounds.Contains(point))
                continue;
            SelectRow(i);
            if (game.Controls.MousePressed)
            {
                var panel = layout.Panel;
                if (entries[i].IsTitle && MenuLayout.BindingPageButton(panel, -1).Contains(point))
                    ChangeEntry(entries[i], -1);
                else
                    ActivateEntry(entries[i]);
            }
            else if (game.Controls.MouseRightPressed && entries[i].Change != null)
            {
                ChangeEntry(entries[i], -1);
            }
            return game.Controls.MousePressed || game.Controls.MouseRightPressed;
        }
        return false;
    }

    private Point? Pointer() =>
        MenuLayout.Pointer(
            game.Controls.MouseNow.Position,
            game.Window.ClientBounds.Width,
            game.Window.ClientBounds.Height
        );

    private MenuLayout.Measurement? MeasurePointer(IReadOnlyList<MenuEntry> entries) =>
        game.Controls.MousePressed || game.Controls.MouseRightPressed || game.Controls.MouseMoved
            ? MenuLayout.Measure(Screen, entries, game.Assets.Font, ShowStickInputs)
            : null;

    private bool ClickBackHint(MenuLayout.Measurement? layout) =>
        layout != null
        && Screen != GameScreen.Main
        && game.Controls.MousePressed
        && Pointer() is { } point
        && MenuLayout.FooterBack(layout.Panel).Contains(point);

    private void UpdateSeats()
    {
        if (game.Controls.MenuDevice() is int device)
        {
            OpenLobbyMenu(device);
            return;
        }
        UpdateLobbyPlayers();
    }

    private void UpdateLobbyPlayers()
    {
        for (int device = 0; device < 10; device++)
        {
            if (device >= 2 && !game.Controls.Pads[device - 2].IsConnected)
                continue;
            bool join =
                device < 2
                    ? game.Controls.Press(game.Settings.Keyboard[device].Attack)
                    : game.Controls.BindingPress(device - 2, game.Controls.ControllerBindings(device - 2).Attack);
            var player = game.Lobby.LocalPlayers.FirstOrDefault(player => player.Id == device);
            if (join)
            {
                if (player?.Spawned != true)
                {
                    game.Lobby.JoinOrSpawn(device);
                    game.Controls.ClearPendingEdges(device);
                }
                else if (game.Lobby.TryChooseAgain(device))
                    game.Controls.ClearPendingEdges(device);
                continue;
            }
            if (player?.Spawned == false)
            {
                bool backOut =
                    device < 2
                        ? game.Controls.Press(game.Settings.Keyboard[device].Jump)
                        : game.Controls.BindingPress(device - 2, game.Controls.ControllerBindings(device - 2).Jump);
                if (backOut)
                {
                    game.Lobby.BackOut(device);
                    menuSoundPending = true;
                    continue;
                }
                bool changeColor =
                    device < 2
                        ? game.Controls.Press(game.Settings.Keyboard[device].Tongue)
                        : game.Controls.BindingPress(device - 2, game.Controls.ControllerBindings(device - 2).Tongue);
                if (changeColor)
                    game.Lobby.ChooseColor(device);
            }
        }
    }

    private void StartFromSeats()
    {
        if (game.Lobby.RosterUpdating)
        {
            game.Toasts.Show("LOBBY UPDATING");
            return;
        }
        var players = game.Lobby.Roster.Slots.Where(slot => slot.Player != null).Select(slot => slot.Player!).ToArray();
        if (players.Length < 2 || players.Any(player => !player.Spawned))
        {
            game.Toasts.Show(players.Length < 2 ? "NEED TWO PLAYERS" : "SPAWN ALL PLAYERS");
            return;
        }
        if (Rules.TeamMode && players.Select(player => player.Team).Distinct().Count() < 2)
        {
            game.Toasts.Show("CHOOSE TWO TEAMS");
            return;
        }
        if (game.Online.Lobby is { } online)
        {
            if (!online.StartMatch(JsonSerializer.Serialize(game.Setup.CreateOptions(game.Options.MapOrder))))
                game.Toasts.Show(online.Notice ?? "HOST ONLY");
        }
        else
            game.StartLocal();
    }

    private void OpenOnline() => Open(GameScreen.Online);

    private void UpdateConnecting(MenuInput input)
    {
        UpdateRows(input);
        if (Screen != GameScreen.Connecting)
            return;
        if (game.Online.Lobby?.Connected == true)
        {
            ShowSeats();
            game.Toasts.Show("LOBBY READY");
        }
    }

    private void UpdateAddress(MenuInput input)
    {
        var entries = Entries();
        if (ClickRow(entries, MeasurePointer(entries)))
            return;
        if (input.Back)
        {
            EditingAddress = false;
            menuSoundPending = true;
            return;
        }
        if (game.Controls.Press(Keys.Back))
        {
            if (Screen == GameScreen.JoinUdp && JoinAddress.Length > 0)
            {
                JoinAddress = JoinAddress[..^1];
                menuSoundPending = true;
            }
        }
        if (input.Accept)
        {
            EditingAddress = false;
            Selected = 1;
            menuSoundPending = true;
        }
    }

    private void BeginAddressEdit()
    {
        EditingAddress = true;
        game.Toasts.Show("ENTER ADDRESS");
    }

    private void Open(GameScreen screen)
    {
        if (screen == GameScreen.CreateLobby)
            Creation.Reset(game.Lobby.Roster);
        if (screen == GameScreen.ViewPlayers)
            selectedPlayer = null;
        if (screen == GameScreen.SlotEditor)
        {
            slotGesture.Reset();
            slotPreview = null;
        }
        history.Push((Screen, Selected));
        Screen = screen;
        Selected = 0;
        if (screen is GameScreen.BrowseSteam or GameScreen.BrowseLan)
            StartBrowser();
    }

    private void OpenLobbyMenu(int device)
    {
        HintDevice = device;
        game.Controls.ClearPendingEdges();
        Open(GameScreen.LobbyMenu);
        menuSoundPending = true;
    }

    private void Back()
    {
        menuSoundPending |= Screen is GameScreen.Playing or GameScreen.Connecting || history.Count > 0;
        EditingAddress = WaitingForBinding = false;
        if (Screen is GameScreen.Settings or GameScreen.Rollback or GameScreen.MatchSettings or GameScreen.Bindings)
            game.SaveSettings();
        if (Screen == GameScreen.Playing)
        {
            Resume();
            return;
        }
        if (Screen == GameScreen.Connecting)
        {
            game.CancelConnection();
            return;
        }
        if (Screen is GameScreen.BrowseSteam or GameScreen.BrowseLan)
            game.Online.StopBrowsing();
        if (!history.TryPop(out var previous))
            return;
        Screen = previous.Screen;
        Selected = previous.Selected;
        game.Controls.ClearPendingEdges();
    }

    private void Resume()
    {
        game.Match.Paused = false;
        game.Controls.ClearPendingEdges();
    }

    private void Reset(GameScreen screen)
    {
        game.Online.StopBrowsing();
        context = Screen = screen;
        history.Clear();
        Selected = 0;
        EditingAddress = WaitingForBinding = false;
        game.Controls.ClearPendingEdges();
    }

    public void ShowMain()
    {
        SelectedSeat = 0;
        Reset(GameScreen.Main);
    }

    public void ShowPlaying() => Reset(GameScreen.Playing);

    public void ShowConnecting()
    {
        Reset(GameScreen.Seats);
        Screen = GameScreen.Connecting;
    }

    public void ShowSeats()
    {
        Reset(GameScreen.Seats);
        if (game.Lobby.World == null)
            game.Lobby.Open();
    }

    private void OpenCredits()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "CREDITS.txt");
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("CREDITS.txt is missing from the game folder.");
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
            when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException
            )
        {
            Console.Error.WriteLine(exception);
            game.Toasts.Show("CANNOT OPEN CREDITS");
        }
    }

    private static int Wrap(int value, int count) => (value % count + count) % count;
}
