using System.Text.Json;
using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

internal static class MenuSetupTests
{
    public static void Run(string contentRoot, Action<bool, string> check)
    {
        var glyphs = Enum.GetValues<Keys>().Select(ButtonGlyph.Key).Distinct().ToArray();
        foreach (var glyph in glyphs)
            check(File.Exists(Path.Combine(contentRoot, glyph.Path + ".xnb")), $"Keyboard icon exists: {glyph.Path}");
        var bakedPaths = glyphs
            .Select(g => g.Path)
            .Where(path => path.StartsWith("UI/Buttons/keyboard/baked/"))
            .ToHashSet();
        var shippedPaths = Directory
            .EnumerateFiles(Path.Combine(contentRoot, "UI/Buttons/keyboard/baked"), "*.xnb")
            .Select(path => Path.ChangeExtension(Path.GetRelativePath(contentRoot, path), null).Replace('\\', '/'));
        check(bakedPaths.SetEquals(shippedPaths), "all baked keyboard icons are reachable from their input keys");

        var settings = ClientSettings.Parse(
            """
            {"Volume":0.35,"MatchDefaults":{"TeamMode":true,"WinScore":12,"MatchRounds":4,
             "CharactersBounceEachOther":true,"FirstMap":3,"ShuffleMaps":true},
             "Keyboard":[{"Jump":27},{"Strafe":78}]}
            """
        );
        var defaults = settings.MatchDefaults;
        check(
            defaults.TeamMode
                && defaults.WinScore == 12
                && defaults.MatchRounds == 4
                && defaults.CharactersBounceEachOther,
            "saved match choices load"
        );
        check(
            defaults.FirstMap == 3 && defaults.ShuffleMaps && settings.Volume == .35f,
            "personal settings and arena choices load independently"
        );
        check(
            settings.Keyboard[0].Jump == Keys.T && settings.Keyboard[1].Strafe == Keys.N,
            "Escape is reserved and the second keyboard retains its strafe binding"
        );
        string saved = JsonSerializer.Serialize(settings);
        check(
            !saved.Contains("PlayerCount") && !saved.Contains("ExpectedPeers") && !saved.Contains("MapOrder"),
            "runtime state and retired rules are not persisted"
        );
        check(ClientSettings.Parse(saved).MatchDefaults == defaults, "new defaults survive settings serialization");
        var current = ClientSettings.Parse("""{"MatchDefaults":{"WinScore":3,"MatchRounds":200,"FirstMap":-1}}""");
        check(
            current.MatchDefaults.WinScore == 3
                && current.MatchDefaults.MatchRounds == 20
                && current.MatchDefaults.FirstMap == 0,
            "invalid match defaults normalize"
        );
        check(ClientSettings.Parse("null").MatchDefaults.MatchRounds == 6, "empty JSON settings use defaults");

        var setup = new MatchSetup(settings, 7);
        check(setup.Lobby.Join(0) && !setup.Lobby.Join(0), "one device cannot claim two seats");
        check(
            setup.Lobby.Join(1) && setup.Lobby.Join(-1) && setup.Lobby.Join(-1),
            "both keyboards and multiple CPUs can join"
        );
        setup.Lobby.SetTeam(1, 5);
        var match = setup.CreateOptions();
        check(match.Rules.MapOrder[0] == 3, "shuffle preserves the selected first arena");
        check(
            match.Rules.PlayerCount == 4 && match.Rules.Teams[1] == 5,
            "match derives its roster from the local party"
        );
        setup.Preferences.WinScore = 17;
        check(
            match.Rules.WinScore == 12 && settings.MatchDefaults.WinScore == 12,
            "editing the lobby does not mutate active rules or saved defaults"
        );
        match.Rules.Teams[0] = 7;
        match.Rules.MapOrder[0] = 6;
        check(
            setup.CreateOptions().Rules.Teams[0] == 0 && setup.CreateOptions().Rules.MapOrder.All(i => i < 6),
            "match snapshots have independent arrays"
        );
        var mapOrder = new[] { 1, 2 };
        setup.CreateOptions(mapOrder).Rules.MapOrder[0] = 6;
        check(mapOrder[0] == 1, "launch map order is not aliased into match rules");
        for (int i = 0; i < 4; i++)
            setup.Lobby.Join(-1);
        check(setup.Seats.Count == 8 && !setup.Lobby.Join(2), "local party respects the eight-player limit");
        setup.Lobby.Remove(0);
        check(
            setup.Seats[0].Device == 1 && setup.Seats[0].Team == 5 && setup.Lobby.Join(2),
            "removal retains remaining device and team assignments and frees a seat"
        );

        KeyboardState keyboard = default;
        GamePadState[] pads = new GamePadState[8];
        MouseState mouse = default;
        var controls = new Controls(new ClientSettings())
        {
            KeyboardSource = () => keyboard,
            GamePadSource = i => pads[i],
            MouseSource = () => mouse,
        };
        pads[1] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.Start);
        controls.Poll();
        check(controls.MenuDevice() == 3, "pause identifies the controller that opened the menu");
        pads[1] = default;
        controls.Poll();
        keyboard = new(Keys.Enter, Keys.Down);
        pads[0] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A | Buttons.DPadDown);
        mouse = new(
            640,
            350,
            0,
            ButtonState.Pressed,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released
        );
        controls.Poll();
        check(MenuInput.Read(controls, 3) == default, "other controllers and keyboard cannot navigate an owned menu");
        check(
            MenuInput.Read(controls, 0).Accept && MenuInput.Read(controls, 0).Vertical == 1 && controls.MousePressed,
            "keyboard owner retains fixed menu input and mouse edges"
        );
        pads[1] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A | Buttons.DPadRight);
        controls.Poll();
        check(
            MenuInput.Read(controls, 3).Accept && MenuInput.Read(controls, 3).Horizontal == 1 && !controls.MousePressed,
            "owning controller can navigate while a held mouse does not repeat"
        );
        keyboard = default;
        pads[1] = default;
        controls.Poll();
        pads[1] = new(new Vector2(1, -1), Vector2.Zero, 0, 0, Buttons.None);
        controls.Poll();
        check(
            MenuInput.Read(controls, 3).Horizontal == 1 && MenuInput.Read(controls, 3).Vertical == 1,
            "left stick navigates both menu axes"
        );
        controls.Poll();
        check(
            MenuInput.Read(controls, 3).Horizontal == 0 && MenuInput.Read(controls, 3).Vertical == 0,
            "held stick does not race through menu items"
        );
        keyboard = new(Keys.W, Keys.D);
        controls.Poll();
        check(
            MenuInput.Read(controls, 0).Vertical == -1 && MenuInput.Read(controls, 0).Horizontal == 1,
            "WASD navigates fixed menus"
        );
        check(MenuInput.Read(controls, 3) == default, "WASD cannot control another player's menu");
        check(
            MenuLayout.Pointer(new Point(480, 384), 960, 768) == new Point(640, 360),
            "mouse accounts for letterboxing and window scale"
        );
        check(MenuLayout.Pointer(new Point(480, 10), 960, 768) == null, "letterbox clicks cannot activate menu rows");
        mouse = new(
            640,
            350,
            0,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Pressed,
            ButtonState.Released,
            ButtonState.Released
        );
        controls.Poll();
        check(controls.MouseRightPressed && !controls.MousePressed, "right click has its own menu input edge");
        controls.Poll();
        check(!controls.MouseRightPressed, "holding right click does not repeatedly change a menu value");
        Console.WriteLine("Settings validation, match isolation, local party and menu input ownership passed");
    }
}
