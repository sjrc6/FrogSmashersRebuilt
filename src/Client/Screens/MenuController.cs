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
    public GameScreen Screen { get; set; } = GameScreen.Intro;
    public int Selected { get; set; }
    public int SelectedSeat { get; private set; }
    public int? Owner { get; private set; }
    public int HintDevice { get; private set; }
    public int BindingDevice { get; private set; }
    public bool AllowLan { get; private set; }
    public bool ShowingLobby => context == GameScreen.Seats && Screen != GameScreen.Error;
    public bool EditingAddress { get; set; }
    public bool WaitingForBinding { get; private set; }
    public string Status { get; set; } = "";
    public string JoinAddress { get; private set; } = "127.0.0.1";
    public string SteamCode { get; private set; } = "";
    public bool ShowingCinematic => Screen is GameScreen.Intro or GameScreen.Title or GameScreen.Outro;
    public bool ShowingMenuBackground => !ShowingCinematic && context == GameScreen.Main && Screen != GameScreen.Error;
    public bool ShowingMatch => !ShowingCinematic && context == GameScreen.Playing && Screen != GameScreen.Error;
    public bool LocalPresentationPaused => ShowingMatch && game.Match.Network == null && game.Match.Paused;
    private bool KeyboardAllowed => Owner == null || Owner < 2;
    private MatchPreferences Rules => game.Setup.Preferences;

    public MenuController(FrogGame game)
    {
        this.game = game;
        game.Setup.Lobby.Roster.SetCapacity(game.Options.Slots);
        AllowLan = game.Options.Lan;
    }

    public void EnterText(char character)
    {
        if (!EditingAddress || char.IsControl(character))
            return;
        if (Screen == GameScreen.JoinSteam && char.IsDigit(character) && SteamCode.Length < 20)
            SteamCode += character;
        else if (
            Screen == GameScreen.JoinUdp
            && (char.IsLetterOrDigit(character) || ".:-".Contains(character))
            && JoinAddress.Length < 128
        )
            JoinAddress += character;
    }

    public void Update(double elapsedSeconds)
    {
        var input = MenuInput.Read(game.Controls, Owner);
        if (Owner.HasValue)
            HintDevice = Owner.Value;
        else if (game.Controls.KeysNow.GetPressedKeys().Any(game.Controls.Press))
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
                }
                break;
            case GameScreen.Title:
                if (input.Accept || game.Controls.Press(Keys.Space))
                    game.MainMenu();
                break;
            case GameScreen.Seats:
                UpdateSeats();
                break;
            case GameScreen.Playing:
                if (!game.Match.Paused)
                {
                    if (game.Controls.MenuDevice() is int device)
                    {
                        Owner = device;
                        game.Match.Paused = true;
                        Selected = 0;
                        game.Controls.ClearPendingEdges();
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
            case GameScreen.Error:
                UpdateRows(input);
                break;
            case GameScreen.Outro:
                game.Match.Network?.Poll();
                if (game.Match.Network?.Error != null && !game.Match.Network.IsTransportFailure)
                    game.Fail(game.Match.Network.Error);
                else if (input.Back || input.Accept || game.Cinematics.Finished)
                    game.ReturnToLobby();
                break;
            case GameScreen.Bindings when WaitingForBinding:
                CaptureBinding(input);
                break;
            case GameScreen.JoinSteam or GameScreen.JoinUdp when EditingAddress:
                UpdateAddress(input);
                break;
            default:
                UpdateRows(input);
                break;
        }

        if (ShowingLobby)
        {
            if (Screen is not (GameScreen.Seats or GameScreen.Connecting))
                UpdateLobbyPlayers(true);
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
    }

    private void UpdateRows(MenuInput input)
    {
        if (input.Back)
        {
            Back();
            return;
        }
        var entries = Entries();
        if (entries.Count == 0)
            return;
        Selected = Wrap(Selected + input.Vertical, entries.Count);
        if (ClickRow(entries))
            return;
        if (input.Horizontal != 0)
            entries[Selected].Change?.Invoke(input.Horizontal);
        else if (input.Accept)
            entries[Selected].Select();
    }

    private bool ClickRow(IReadOnlyList<MenuEntry> entries)
    {
        if (
            !KeyboardAllowed
            || !(game.Controls.MousePressed || game.Controls.MouseMoved)
            || Pointer() is not Point point
        )
            return false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (!MenuLayout.Row(Screen, i, entries.Count, SelectedSeat).Contains(point))
                continue;
            Selected = i;
            if (game.Controls.MousePressed)
                entries[i].Select();
            return game.Controls.MousePressed;
        }
        return false;
    }

    private Point? Pointer() =>
        MenuLayout.Pointer(
            game.Controls.MouseNow.Position,
            game.Window.ClientBounds.Width,
            game.Window.ClientBounds.Height
        );

    private void UpdateSeats()
    {
        if (game.Controls.Press(Keys.Escape))
        {
            OpenOwned(GameScreen.LobbyMenu, 0);
            return;
        }
        UpdateLobbyPlayers(false);
    }

    private void UpdateLobbyPlayers(bool menuOpen)
    {
        for (int device = 0; device < 10; device++)
        {
            if (menuOpen && Owner is int owner && (owner == device || owner < 2 && device < 2))
                continue;
            bool start =
                device < 2
                    ? game.Controls.Press(device == 0 ? Keys.Space : Keys.RightShift)
                    : game.Controls.PadPress(device - 2, Buttons.Start);
            var player = game.Lobby.LocalPlayers.FirstOrDefault(player => player.Id == device);
            if (start)
            {
                if (player?.Spawned == true && device >= 2)
                {
                    if (menuOpen)
                        continue;
                    OpenOwned(GameScreen.LobbyMenu, device);
                    return;
                }
                game.Lobby.JoinOrSpawn(device);
            }
            if (player?.Spawned == false)
                game.Lobby.Choose(
                    device,
                    (
                        device < 2
                            ? game.Controls.Press(game.Settings.Keyboard[device].Tongue)
                            : game.Controls.PadPress(device - 2, Buttons.B)
                    )
                        ? 1
                        : 0,
                    game.Lobby.TeamMode ? game.Controls.Vertical(device) : 0
                );
        }
    }

    private void StartFromSeats()
    {
        var players = game.Lobby.Roster.Slots.Where(slot => slot.Player != null).Select(slot => slot.Player!).ToArray();
        if (players.Length < 2 || players.Any(player => !player.Spawned))
        {
            Status = "SPAWN AT LEAST TWO PLAYERS; ALL JOINED PLAYERS MUST BE READY";
            return;
        }
        if (Rules.TeamMode && players.Select(player => player.Team).Distinct().Count() < 2)
        {
            Status = "CHOOSE AT LEAST TWO DIFFERENT TEAMS";
            return;
        }
        if (game.Online.Lobby is { } online)
        {
            if (!online.StartMatch(JsonSerializer.Serialize(game.Setup.CreateOptions(game.Options.MapOrder))))
                Status = online.Notice ?? "THE HOST STARTS THE MATCH";
        }
        else
            game.StartLocal();
    }

    private void OpenOnline()
    {
        game.Online.PrepareSteam();
        Open(GameScreen.Online);
    }

    private void UpdateConnecting(MenuInput input)
    {
        UpdateRows(input);
        if (Screen != GameScreen.Connecting)
            return;
        if (game.Online.Lobby?.Connected == true)
            ShowSeats();
    }

    private void UpdateRoomSelection(MenuInput input)
    {
        if (input.Back)
        {
            Back();
            return;
        }
        int cell = MenuLayout.RoomCell(SelectedSeat);
        int x = cell % 3,
            y = cell / 3;
        if (input.Horizontal != 0)
        {
            do
            {
                x = Math.Clamp(x + input.Horizontal, 0, 2);
            } while (y == 1 && x == 1);
        }
        if (input.Vertical != 0)
        {
            do
            {
                y = Math.Clamp(y + input.Vertical, 0, 2);
            } while (y == 1 && x == 1);
        }
        int target = y * 3 + x;
        SelectedSeat = target > 4 ? target - 1 : target;
        if (KeyboardAllowed && (game.Controls.MouseMoved || game.Controls.MousePressed) && Pointer() is Point point)
        {
            for (int room = 0; room < 8; room++)
                if (MenuLayout.Room(room).Contains(point))
                {
                    SelectedSeat = room;
                    if (game.Controls.MousePressed)
                        Open(GameScreen.SlotOptions);
                    return;
                }
        }
        if (input.Accept)
            Open(GameScreen.SlotOptions);
    }

    private void EditRoom(SlotType type, bool open, bool remove = false)
    {
        if (!game.Lobby.Edit(SelectedSeat, type, open, remove))
            Status = "COULD NOT EDIT SLOT";
    }

    private void ReturnFromLobbyMenu()
    {
        Reset(GameScreen.Seats);
    }

    private void UpdateAddress(MenuInput input)
    {
        if (ClickRow(Entries()))
            return;
        if (input.Back)
        {
            EditingAddress = false;
            return;
        }
        if (game.Controls.Press(Keys.Back))
        {
            if (Screen == GameScreen.JoinSteam && SteamCode.Length > 0)
                SteamCode = SteamCode[..^1];
            if (Screen == GameScreen.JoinUdp && JoinAddress.Length > 0)
                JoinAddress = JoinAddress[..^1];
        }
        if (input.Accept)
        {
            EditingAddress = false;
            Selected = 1;
        }
    }

    private void OpenBindings(int device)
    {
        BindingDevice = device;
        WaitingForBinding = false;
        Open(GameScreen.Bindings);
    }

    public Keys[] BindingKeys()
    {
        var keys = game.Settings.Keyboard[BindingDevice];
        return [keys.Left, keys.Right, keys.Up, keys.Down, keys.Jump, keys.Attack, keys.Tongue, keys.Strafe];
    }

    private void CaptureBinding(MenuInput input)
    {
        if (input.Back)
        {
            WaitingForBinding = false;
            return;
        }
        if (!KeyboardAllowed)
            return;
        var pressed = game.Controls.KeysNow.GetPressedKeys().Where(game.Controls.Press).ToArray();
        if (pressed.Length == 0)
            return;
        var key = pressed[0];
        if (key == Keys.Escape)
        {
            WaitingForBinding = false;
            return;
        }
        var keys = game.Settings.Keyboard[BindingDevice];
        switch (Selected)
        {
            case 0:
                keys.Left = key;
                break;
            case 1:
                keys.Right = key;
                break;
            case 2:
                keys.Up = key;
                break;
            case 3:
                keys.Down = key;
                break;
            case 4:
                keys.Jump = key;
                break;
            case 5:
                keys.Attack = key;
                break;
            case 6:
                keys.Tongue = key;
                break;
            case 7:
                keys.Strafe = key;
                break;
        }
        WaitingForBinding = false;
        game.Controls.ClearPendingEdges();
    }

    private void Open(GameScreen screen)
    {
        history.Push((Screen, Selected));
        Screen = screen;
        Selected = 0;
        Status = "";
    }

    private void OpenOwned(GameScreen screen, int device)
    {
        Owner = device;
        game.Controls.ClearPendingEdges();
        Open(screen);
    }

    private void Back()
    {
        EditingAddress = WaitingForBinding = false;
        if (Screen is GameScreen.Settings or GameScreen.MatchSettings or GameScreen.Bindings)
            game.SaveSettings();
        if (Screen == GameScreen.Playing)
        {
            Resume();
            return;
        }
        if (Screen == GameScreen.Connecting)
        {
            game.ReturnToLobby();
            return;
        }
        if (Screen == GameScreen.Error)
        {
            game.MainMenu();
            return;
        }
        if (!history.TryPop(out var previous))
            return;
        Screen = previous.Screen;
        Selected = previous.Selected;
        Status = "";
        if (Screen == GameScreen.Seats)
            Owner = null;
        game.Controls.ClearPendingEdges();
    }

    private void Resume()
    {
        game.Match.Paused = false;
        Owner = null;
        game.Controls.ClearPendingEdges();
    }

    private void Reset(GameScreen screen)
    {
        context = Screen = screen;
        history.Clear();
        Selected = 0;
        Owner = null;
        EditingAddress = WaitingForBinding = false;
        Status = "";
        game.Controls.ClearPendingEdges();
    }

    public void ShowMain() => Reset(GameScreen.Main);

    public void ShowPlaying() => Reset(GameScreen.Playing);

    public void ShowConnecting()
    {
        var owner = Owner;
        Reset(GameScreen.Seats);
        Screen = GameScreen.Connecting;
        Owner = owner;
    }

    public void ShowSeats(bool online = false)
    {
        Reset(GameScreen.Seats);
        if (game.Lobby.World == null)
            game.Lobby.Open();
        if (online)
        {
            Owner = 0;
            OpenOnline();
        }
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
            game.Fail($"Could not open CREDITS.txt. You can open it from the game folder.\n{exception.Message}");
        }
    }

    private static int Wrap(int value, int count) => (value % count + count) % count;
}
