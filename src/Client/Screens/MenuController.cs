using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal sealed class MenuController
{
    private readonly FrogGame game;
    private static readonly int[] FrameRates = [60, 90, 120, 144, 165, 240, 360, 500];

    private enum Setting
    {
        Fullscreen,
        VSync,
        FrameLimit,
        Volume,
        ScreenShake,
        TeamMode,
        WinScore,
        MatchRounds,
        FirstMap,
        MapOrder,
        FontSmoothing,
        KeyboardOne,
        KeyboardTwo,
        Save,
        Count,
    }

    public GameScreen Screen { get; set; } = GameScreen.Intro;
    public GameScreen SettingsReturn { get; private set; } = GameScreen.Main;
    public int Selected { get; set; }
    public int SettingRow { get; private set; }
    public int BindingDevice { get; private set; }
    public int BindingRow { get; private set; }
    public int ExpectedPeers { get; set; }
    public bool OnlineSeats { get; set; }
    public bool EditingAddress { get; set; }
    public bool WaitingForBinding { get; private set; }
    public string Status { get; set; } = "";
    public string JoinAddress { get; private set; } = "127.0.0.1";
    public string SteamCode { get; private set; } = "";
    public bool ShowingCinematic =>
        Screen is GameScreen.Intro or GameScreen.Title or GameScreen.Main or GameScreen.Credits or GameScreen.Outro;
    public bool ShowingMatch =>
        Screen == GameScreen.Playing
        || SettingsReturn == GameScreen.Playing && Screen is GameScreen.Settings or GameScreen.Bindings;
    public bool LocalPresentationPaused =>
        game.Match.Network == null
        && game.Match.World != null
        && (
            Screen == GameScreen.Playing && game.Match.Paused
            || SettingsReturn == GameScreen.Playing && Screen is GameScreen.Settings or GameScreen.Bindings
        );

    public MenuController(FrogGame game)
    {
        this.game = game;
        ExpectedPeers = game.Options.Host != null ? game.Options.Peers : game.Settings.ExpectedPeers;
    }

    public void EnterText(char character)
    {
        if (!EditingAddress || char.IsControl(character))
        {
            return;
        }

        if (Selected == 1 && char.IsDigit(character) && SteamCode.Length < 20)
        {
            SteamCode += character;
        }
        else if (
            Selected == 3
            && (char.IsLetterOrDigit(character) || ".:-".Contains(character))
            && JoinAddress.Length < 128
        )
        {
            JoinAddress += character;
        }
    }

    public void Update(double elapsedSeconds)
    {
        var input = MenuInput.Read(game.Controls);
        switch (Screen)
        {
            case GameScreen.Intro:
                UpdateIntro(input);
                break;
            case GameScreen.Title:
                UpdateTitle(input);
                break;
            case GameScreen.Main:
                UpdateMain(input);
                break;
            case GameScreen.Seats:
                UpdateSeats(input);
                break;
            case GameScreen.Online:
                UpdateOnline(input);
                break;
            case GameScreen.Connecting:
                UpdateConnecting(input);
                break;
            case GameScreen.Playing:
                UpdatePlaying(input, elapsedSeconds);
                break;
            case GameScreen.Settings:
                UpdateSettings(input, elapsedSeconds);
                break;
            case GameScreen.Bindings:
                UpdateBindings(input, elapsedSeconds);
                break;
            case GameScreen.Credits:
                UpdateCredits(input);
                break;
            case GameScreen.Error:
                UpdateError(input);
                break;
            case GameScreen.Outro:
                UpdateOutro(input);
                break;
        }
    }

    private void UpdateIntro(MenuInput input)
    {
        if (input.Accept || input.Back || game.Controls.Press(Keys.Space) || game.Cinematics.TitleReady)
        {
            game.Cinematics.SkipIntro();
            Screen = GameScreen.Title;
        }
    }

    private void UpdateTitle(MenuInput input)
    {
        if (input.Accept || game.Controls.Press(Keys.Space))
        {
            game.MainMenu();
        }
    }

    private void UpdateMain(MenuInput input)
    {
        Selected = Wrap(Selected + input.Vertical, 7);
        if (input.Accept)
        {
            switch (Selected)
            {
                case 0:
                    OnlineSeats = false;
                    game.Setup.Seats.Clear();
                    Screen = GameScreen.Seats;
                    Selected = 0;
                    return;
                case 1:
                    OnlineSeats = true;
                    game.Setup.Seats.Clear();
                    Screen = GameScreen.Seats;
                    Selected = 0;
                    game.Online.PrepareSteam();
                    return;
                case 2:
                    OpenSettings(GameScreen.Main);
                    return;
                case 3:
                    game.Cinematics.StartIntro();
                    Screen = GameScreen.Intro;
                    return;
                case 4:
                    Screen = GameScreen.Credits;
                    return;
                case 5:
                    game.Setup.Seats.Clear();
                    for (int i = 0; i < 4; i++)
                    {
                        game.Setup.Seats.Add(new(-1, i % 2));
                    }

                    game.StartLocal();
                    return;
                case 6:
                    game.Exit();
                    return;
            }
        }
    }

    private void UpdateSeats(MenuInput input)
    {
        if (input.Back)
        {
            game.MainMenu();
            return;
        }

        bool controllerStart = Enumerable
            .Range(0, 8)
            .Any(p => game.Controls.PadPress(p, Buttons.Start) && game.Setup.Seats.Any(s => s.Device == p + 2));
        if (game.Controls.Press(Keys.Space))
        {
            JoinDevice(0);
        }

        if (game.Controls.Press(Keys.RightShift))
        {
            JoinDevice(1);
        }

        for (int p = 0; p < 8; p++)
        {
            if (game.Controls.PadPress(p, Buttons.Start) || game.Controls.PadPress(p, Buttons.A))
            {
                JoinDevice(p + 2);
            }
        }

        if (game.Controls.Press(Keys.C) && game.Setup.Seats.Count < 8)
        {
            game.Setup.Seats.Add(new(-1, OnlineSeats ? -1 : game.Setup.Seats.Count % 2));
        }

        if (game.Controls.Press(Keys.Back) && game.Setup.Seats.Count > 0)
        {
            game.Setup.Seats.RemoveAt(game.Setup.Seats.Count - 1);
        }

        if (game.Setup.Seats.Count > 0)
        {
            Selected = Wrap(Selected + input.Vertical, game.Setup.Seats.Count);
            if (input.Horizontal != 0)
            {
                game.Setup.Seats[Selected] = game.Setup.Seats[Selected] with
                {
                    Team = OnlineSeats
                        ? Wrap(game.Setup.Seats[Selected].Team + 1 + input.Horizontal, 9) - 1
                        : Wrap(game.Setup.Seats[Selected].Team + input.Horizontal, 8),
                };
            }
        }

        if (game.Controls.Press(Keys.Tab))
        {
            OpenSettings(GameScreen.Seats);
            return;
        }

        bool start = game.Controls.Press(Keys.Enter) || controllerStart;
        if (start && game.Setup.Seats.Count >= (OnlineSeats ? 1 : 2))
        {
            if (OnlineSeats)
            {
                Screen = GameScreen.Online;
                Selected = 0;
            }
            else
            {
                game.StartLocal();
            }
        }
    }

    private void UpdateOnline(MenuInput input)
    {
        if (input.Back)
        {
            if (EditingAddress)
            {
                EditingAddress = false;
            }
            else
            {
                Screen = GameScreen.Seats;
            }

            return;
        }

        if (!EditingAddress)
        {
            Selected = Wrap(Selected + input.Vertical, 5);
        }

        if (input.Horizontal != 0 && !EditingAddress)
        {
            ExpectedPeers = Math.Clamp(ExpectedPeers + input.Horizontal, 2, 8);
        }

        if (EditingAddress && game.Controls.Press(Keys.Back))
        {
            if (Selected == 1 && SteamCode.Length > 0)
            {
                SteamCode = SteamCode[..^1];
            }

            if (Selected == 3 && JoinAddress.Length > 0)
            {
                JoinAddress = JoinAddress[..^1];
            }
        }

        if (input.Accept)
        {
            if (Selected == 0)
            {
                game.BeginLobby("steam", true);
            }

            if (Selected == 2)
            {
                game.BeginLobby("udp", true);
            }

            if (Selected is 1 or 3)
            {
                if (!EditingAddress)
                {
                    EditingAddress = true;
                }
                else
                {
                    EditingAddress = false;
                    game.BeginLobby(Selected == 1 ? "steam:" + SteamCode : "udp:" + JoinAddress, false);
                }
            }

            if (Selected == 4)
            {
                Screen = GameScreen.Seats;
            }
        }
    }

    private void UpdateConnecting(MenuInput input)
    {
        if (input.Back)
        {
            game.MainMenu();
            return;
        }

        game.Online.Lobby!.Poll();
        if (game.Online.Lobby is SteamLobby sl && game.Controls.Press(Keys.I))
        {
            sl.InviteFriends();
        }

        if (game.Online.Lobby.Error != null)
        {
            game.Fail(game.Online.Lobby.Error);
            return;
        }

        if (game.Online.Lobby.Ready)
        {
            game.StartNetwork();
        }
    }

    private void UpdatePlaying(MenuInput input, double elapsedSeconds)
    {
        if (game.Match.Paused && game.Controls.Press(Keys.Q))
        {
            game.MainMenu();
            return;
        }

        if (game.Match.Paused && game.Controls.Press(Keys.Tab))
        {
            OpenSettings(GameScreen.Playing);
            return;
        }

        game.AdvanceMatch(elapsedSeconds);
        if (
            Screen == GameScreen.Playing
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

    private void UpdateSettings(MenuInput input, double elapsedSeconds)
    {
        SettingRow = Wrap(SettingRow + input.Vertical, (int)Setting.Count);
        if (input.Back || input.Accept && SettingRow == (int)Setting.Save)
        {
            game.SaveSettings();
            Screen = SettingsReturn;
            return;
        }

        if (input.Accept && (Setting)SettingRow is Setting.KeyboardOne or Setting.KeyboardTwo)
        {
            BindingDevice = SettingRow - (int)Setting.KeyboardOne;
            BindingRow = 0;
            WaitingForBinding = false;
            Screen = GameScreen.Bindings;
            return;
        }

        if (input.Horizontal != 0 || input.Accept)
        {
            ChangeSetting(input.Horizontal == 0 ? 1 : input.Horizontal);
        }

        if (SettingsReturn == GameScreen.Playing && game.Match.Network != null)
        {
            game.AdvanceMatch(elapsedSeconds);
        }
    }

    private void UpdateBindings(MenuInput input, double elapsedSeconds)
    {
        if (WaitingForBinding)
        {
            var keys = game.Controls.KeysNow.GetPressedKeys().Where(k => game.Controls.KeysBefore.IsKeyUp(k)).ToArray();
            if (keys.Length > 0)
            {
                if (keys[0] != Keys.Escape)
                {
                    SetBinding(keys[0]);
                }

                WaitingForBinding = false;
            }
        }
        else if (input.Back)
        {
            Screen = GameScreen.Settings;
        }
        else
        {
            BindingRow = Wrap(BindingRow + input.Vertical, 8);
            if (input.Accept)
            {
                WaitingForBinding = true;
            }
        }

        if (SettingsReturn == GameScreen.Playing && game.Match.Network != null)
        {
            game.AdvanceMatch(elapsedSeconds);
        }
    }

    private void UpdateCredits(MenuInput input)
    {
        if (input.Back || input.Accept)
        {
            game.MainMenu();
        }
    }

    private void UpdateError(MenuInput input)
    {
        if (input.Back || input.Accept)
        {
            game.MainMenu();
        }
    }

    private void UpdateOutro(MenuInput input)
    {
        game.Match.Network?.Poll();
        if (game.Match.Network?.Error != null && !game.Match.Network.IsTransportFailure)
        {
            game.Fail(game.Match.Network.Error);
            return;
        }

        if (input.Back || input.Accept || game.Cinematics.Finished)
        {
            game.MainMenu();
        }
    }

    public void ShowMain()
    {
        Screen = GameScreen.Main;
        Selected = 0;
        EditingAddress = false;
        Status = "";
    }

    private void JoinDevice(int device)
    {
        var seats = game.Setup.Seats;
        if (seats.Count < 8 && !seats.Any(seat => seat.Device == device))
        {
            seats.Add(new LocalSeat(device, OnlineSeats ? -1 : seats.Count % 2));
        }
    }

    private void OpenSettings(GameScreen returnScreen)
    {
        SettingsReturn = returnScreen;
        Screen = GameScreen.Settings;
        SettingRow = 0;
    }

    private void ChangeSetting(int amount)
    {
        switch ((Setting)SettingRow)
        {
            case Setting.Fullscreen:
                game.Settings.Fullscreen = !game.Settings.Fullscreen;
                game.ApplyDisplay();
                break;
            case Setting.VSync:
                game.Settings.VSync = !game.Settings.VSync;
                game.ApplyDisplay();
                break;
            case Setting.FrameLimit:
                game.Settings.FrameLimit = FrameRates[
                    Wrap(Array.IndexOf(FrameRates, game.Settings.FrameLimit) + amount, FrameRates.Length)
                ];
                break;
            case Setting.Volume:
                game.Settings.Volume = Math.Clamp(game.Settings.Volume + amount * .1f, 0, 1);
                game.Audio.Volume = game.Settings.Volume;
                break;
            case Setting.ScreenShake:
                game.Settings.ScreenShake = !game.Settings.ScreenShake;
                game.Renderer.ShakeEnabled = game.Cinematics.ShakeEnabled = game.Settings.ScreenShake;
                break;
            case Setting.TeamMode:
                game.Setup.Options.Rules.TeamMode = !game.Setup.Options.Rules.TeamMode;
                break;
            case Setting.WinScore:
                game.Setup.Options.Rules.WinScore = Wrap(game.Setup.Options.Rules.WinScore + amount, 31);
                break;
            case Setting.MatchRounds:
                game.Setup.Options.Rules.MatchRounds = Math.Clamp(game.Setup.Options.Rules.MatchRounds + amount, 1, 20);
                break;
            case Setting.FirstMap:
                game.Setup.FirstMap = Wrap(game.Setup.FirstMap + amount, 7);
                break;
            case Setting.MapOrder:
                game.Setup.ShuffleMaps = !game.Setup.ShuffleMaps;
                break;
            case Setting.FontSmoothing:
                game.Settings.FontSmoothing = Wrap(game.Settings.FontSmoothing + amount, 3);
                game.Renderer.TextEdgeWidth = game.FontEdgeWidth;
                break;
        }
    }

    private void SetBinding(Keys key)
    {
        var k = game.Settings.Keyboard[BindingDevice];
        switch (BindingRow)
        {
            case 0:
                k.Left = key;
                break;
            case 1:
                k.Right = key;
                break;
            case 2:
                k.Up = key;
                break;
            case 3:
                k.Down = key;
                break;
            case 4:
                k.Jump = key;
                break;
            case 5:
                k.Attack = key;
                break;
            case 6:
                k.Tongue = key;
                break;
            case 7:
                k.Strafe = key;
                break;
        }
    }

    private static int Wrap(int value, int count) => (value % count + count) % count;
}
