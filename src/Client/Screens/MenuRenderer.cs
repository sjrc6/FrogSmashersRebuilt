using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class MenuRenderer
{
    private static readonly Color Muted = new(173, 190, 200);
    private static readonly Color Accent = new(255, 211, 86);
    private static readonly Color PanelColor = new(14, 23, 29, 236);
    private readonly FrogGame game;
    private readonly MenuController menu;

    public MenuRenderer(FrogGame game, MenuController menu)
    {
        this.game = game;
        this.menu = menu;
    }

    public void Draw()
    {
        switch (menu.Screen)
        {
            case GameScreen.Intro:
                break;
            case GameScreen.Title:
                break;
            case GameScreen.Main:
                game.Renderer.Panel(new Rectangle(0, 0, Renderer.Width, Renderer.Height), Color.Black * .15f);
                game.Renderer.Image(
                    "Textures/Sprites/logo_rebuilt",
                    new Rectangle(50, 39, 160, 97),
                    new Vector2(640, 22),
                    480
                );
                game.Renderer.Panel(new Rectangle(440, 332, 400, 310), new Color(14, 23, 29, 210));
                Menu(
                    ["LOCAL", "ONLINE", "SETTINGS", "PLAY INTRO", "WATCH CPUS", "CREDITS", "QUIT"],
                    menu.Selected,
                    352,
                    38
                );
                break;
            case GameScreen.Seats:
                DrawSeats();
                break;
            case GameScreen.Online:
                Header("ONLINE MATCH");
                game.Renderer.Panel(new Rectangle(220, 145, 840, 435), PanelColor);
                game.Renderer.Text(
                    $"{game.Setup.Seats.Count} LOCAL SEAT(S)   /   {menu.ExpectedPeers} NETWORK PEERS",
                    640,
                    177,
                    Accent,
                    1,
                    true
                );
                Menu(
                    [
                        "HOST STEAM PRIVATE LOBBY",
                        "JOIN STEAM: " + (menu.SteamCode.Length > 0 ? menu.SteamCode : "ENTER LOBBY ID"),
                        "HOST LOCALHOST / UDP",
                        "JOIN UDP: " + menu.JoinAddress,
                        "BACK",
                    ],
                    menu.Selected,
                    250,
                    50,
                    .95f
                );
                Footer(
                    menu.EditingAddress
                        ? "TYPE ADDRESS / ID, ENTER TO CONNECT, ESC TO CANCEL"
                        : "LEFT/RIGHT: PEER COUNT   |   ENTER: SELECT   |   ESC: BACK"
                );
                break;
            case GameScreen.Connecting:
                Header("CONNECTING");
                game.Renderer.Panel(new Rectangle(150, 210, 980, 255), PanelColor);
                game.Renderer.Text(game.Online.Lobby?.Status ?? "", 640, 255, Color.White, 1.2f, true);
                if (game.Online.Lobby is SteamLobby sl)
                {
                    game.Renderer.Text("LOBBY ID: " + sl.LobbyCode, 640, 315, Accent, 1, true);
                    game.Renderer.Text("I: INVITE STEAM FRIENDS", 640, 370, Muted, 1, true);
                }
                else
                {
                    game.Renderer.Text(
                        $"UDP PORT {game.Options.Port}  /  {menu.ExpectedPeers} PEERS",
                        640,
                        325,
                        Muted,
                        1,
                        true
                    );
                }

                Footer("MATCH STARTS WHEN ALL PEERS CONNECT   |   ESC: CANCEL");
                break;
            case GameScreen.Playing:
                if (game.Match.Paused)
                {
                    game.Renderer.Panel(new Rectangle(290, 240, 700, 190), PanelColor);
                    game.Renderer.Text(
                        game.Match.Network == null ? "PAUSED" : "MENU - ONLINE MATCH CONTINUES",
                        640,
                        275,
                        Accent,
                        1.2f,
                        true
                    );
                    game.Renderer.Text(
                        "ESC: RESUME   TAB: SETTINGS   Q: LEAVE MATCH",
                        640,
                        345,
                        Color.White,
                        .9f,
                        true
                    );
                }

                if (game.Match.World?.Phase == MatchPhase.MatchFinished && !game.Match.Paused)
                {
                    game.Renderer.Text(
                        game.Match.Network == null ? "ENTER: OUTRO   |   ESC: MENU" : "ESC THEN Q: LEAVE MATCH",
                        640,
                        440,
                        Accent,
                        1,
                        true
                    );
                }

                break;
            case GameScreen.Settings:
                DrawSettings();
                break;
            case GameScreen.Bindings:
                DrawBindings();
                break;
            case GameScreen.Error:
                Header("COULD NOT CONTINUE");
                game.Renderer.Panel(new Rectangle(120, 190, 1040, 320), PanelColor);
                DrawWrapped(menu.Status, 165, 235, 940, Color.White);
                Footer("ENTER / ESC: RETURN TO MENU");
                break;
            case GameScreen.Outro:
                Footer("ENTER / ESC: MAIN MENU");
                break;
        }
    }

    private void Header(string text)
    {
        game.Renderer.Panel(new Rectangle(0, 0, 1280, 107), PanelColor);
        game.Renderer.Text(text, 640, 40, Color.White, 1.6f, true);
    }

    private void Footer(string text) => game.Renderer.Text(text, 640, 679, Muted, .8f, true);

    private void Menu(string[] items, int chosen, float top, float spacing, float scale = 1)
    {
        for (int i = 0; i < items.Length; i++)
        {
            game.Renderer.Text(
                (i == chosen ? "> " : "  ") + items[i],
                640,
                top + i * spacing,
                i == chosen ? Accent : Color.White,
                scale,
                true
            );
        }
    }

    private void DrawSeats()
    {
        Header(menu.OnlineSeats ? "JOIN LOCAL SEATS FOR ONLINE" : "LOCAL MATCH");
        for (int i = 0; i < 8; i++)
        {
            int col = i % 4;
            int row = i / 4;
            int x = 85 + col * 285;
            int y = 155 + row * 182;
            game.Renderer.Panel(new Rectangle(x, y, 255, 154), PanelColor);
            if (i < game.Setup.Seats.Count)
            {
                var s = game.Setup.Seats[i];
                game.Renderer.Text(
                    $"{(i == menu.Selected ? "> " : "")}PLAYER {i + 1}",
                    x + 128,
                    y + 18,
                    game.Setup.Rules.TeamMode && s.Team >= 0 ? Renderer.TeamColor(s.Team) : Renderer.PlayerColors[i],
                    1.2f,
                    true
                );
                game.Renderer.Text(s.Label, x + 128, y + 68, Color.White, .9f, true);
                game.Renderer.Text(
                    game.Setup.Rules.TeamMode || menu.OnlineSeats
                        ? s.Team < 0
                            ? "TEAM: AUTO"
                            : $"TEAM {s.Team + 1}"
                        : "READY",
                    x + 128,
                    y + 109,
                    Accent,
                    .85f,
                    true
                );
            }
            else
            {
                game.Renderer.Text("OPEN SLOT", x + 128, y + 62, Muted, 1, true);
            }
        }

        game.Renderer.Text(
            "SPACE: KEYBOARD 1    RIGHT SHIFT: KEYBOARD 2    A / START: CONTROLLER",
            640,
            558,
            Color.White,
            .85f,
            true
        );
        game.Renderer.Text(
            "C: ADD CPU   BACKSPACE: REMOVE LAST   TAB: RULES   ENTER: START",
            640,
            601,
            Accent,
            .85f,
            true
        );
        if (menu.Status.Length > 0)
        {
            game.Renderer.Text(menu.Status, 640, 640, Accent, .75f, true);
        }

        Footer(
            game.Setup.Rules.TeamMode || menu.OnlineSeats
                ? "UP/DOWN: SELECT SEAT   LEFT/RIGHT: TEAM   ESC: BACK"
                : "WASD + T/U/Y   |   ARROWS + M/PERIOD/COMMA   |   PAD: A/X/B"
        );
    }

    private void DrawSettings()
    {
        Header("SETTINGS");
        game.Renderer.Panel(new Rectangle(245, 127, 790, 510), PanelColor);
        string[] values =
        [
            "BORDERLESS FULLSCREEN: " + OnOff(game.Settings.Fullscreen),
            "VSYNC: " + OnOff(game.Settings.VSync),
            "FRAME LIMIT: " + game.Settings.FrameLimit,
            "VOLUME: " + (int)(game.Settings.Volume * 100) + "%",
            "SCREEN SHAKE: " + OnOff(game.Settings.ScreenShake),
            "TEAMS (NEXT MATCH): " + OnOff(game.Setup.Rules.TeamMode),
            "WIN SCORE: " + (game.Setup.Rules.WinScore == 0 ? "ORIGINAL DEFAULT" : game.Setup.Rules.WinScore),
            "MATCH ROUNDS: " + game.Setup.Rules.MatchRounds,
            "FIRST ARENA: " + game.Assets.Data.Maps[game.Setup.FirstMap].Name,
            "MAP ORDER: " + (game.Setup.ShuffleMaps ? "SHUFFLED" : "SEQUENTIAL"),
            "FONT SMOOTHING: " + new[] { "OFF", "NARROW", "NORMAL" }[game.Settings.FontSmoothing],
            "KEYBOARD 1 BINDINGS",
            "KEYBOARD 2 BINDINGS",
            "SAVE AND BACK",
        ];
        Menu(values, menu.SettingRow, 147, 34, .85f);
        Footer("UP/DOWN: SELECT   LEFT/RIGHT: CHANGE   ENTER: SELECT   ESC: SAVE/BACK");
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";

    private void DrawBindings()
    {
        Header($"KEYBOARD {menu.BindingDevice + 1}");
        game.Renderer.Panel(new Rectangle(280, 145, 720, 465), PanelColor);
        var k = game.Settings.Keyboard[menu.BindingDevice];
        Menu(
            [
                "LEFT: " + k.Left,
                "RIGHT: " + k.Right,
                "UP: " + k.Up,
                "DOWN: " + k.Down,
                "JUMP: " + k.Jump,
                "BAT: " + k.Attack,
                "TONGUE: " + k.Tongue,
                "STRAFE: " + k.Strafe,
            ],
            menu.BindingRow,
            180,
            47
        );
        Footer(menu.WaitingForBinding ? "PRESS A KEY (ESC CANCELS)" : "UP/DOWN: SELECT   ENTER: REBIND   ESC: BACK");
    }

    private void DrawWrapped(string text, float x, float y, float width, Color color)
    {
        string line = "";
        foreach (var word in text.Split(' '))
        {
            if (game.Assets.Font.Measure(line + word, .9f).X > width)
            {
                game.Renderer.Text(line, x, y, color, .9f);
                y += 32;
                line = "";
            }

            line += word + " ";
        }

        game.Renderer.Text(line, x, y, color, .9f);
    }
}
