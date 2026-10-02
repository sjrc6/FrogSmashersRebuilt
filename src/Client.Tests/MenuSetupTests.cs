using System.Text.Json;
using FrogSmashers.Client;
using FrogSmashers.Network;
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

        var settings = ClientSettings.Parse("""{"Volume":0.35,"Keyboard":[{"Jump":27},{"Strafe":78}]}""");
        check(settings.Volume == .35f, "personal settings load");
        check(
            settings.Keyboard[0].Jump == Keys.T && settings.Keyboard[1].Strafe == Keys.N,
            "Escape is reserved and the second keyboard retains its strafe binding"
        );
        string saved = JsonSerializer.Serialize(settings);
        check(
            !saved.Contains("Match") && !saved.Contains("PlayerCount") && !saved.Contains("MapOrder"),
            "match and lobby state are not persisted"
        );
        check(ClientSettings.Parse(saved).Volume == settings.Volume, "personal settings survive serialization");
        check(
            settings.Rollback.Delay == 2 && settings.Rollback.Donation == 0 && settings.Rollback.MaxExtraDelay == 0,
            "rollback defaults are two response ticks, zero donation and no automatic extra delay"
        );
        settings.Rollback = new()
        {
            Delay = 7,
            Donation = 4,
            MaxExtraDelay = 12,
        };
        check(
            ClientSettings.Parse(JsonSerializer.Serialize(settings)).Rollback == settings.Rollback,
            "personal rollback preferences persist"
        );
        check(
            ClientSettings.Parse("""{"Rollback":{"Delay":-1,"Donation":999,"MaxExtraDelay":999}}""").Rollback
                == new RollbackPreferences
                {
                    Delay = 0,
                    Donation = 30,
                    MaxExtraDelay = 119,
                },
            "rollback settings load within supported limits"
        );
        check(
            ClientSettings.Parse("""{"Rollback":null}""").Rollback == new RollbackPreferences(),
            "null rollback settings load defaults"
        );
        check(
            RollbackPreferences.Milliseconds(0) == "0.00 MS"
                && RollbackPreferences.Milliseconds(1) == "8.33 MS"
                && RollbackPreferences.Milliseconds(2) == "16.7 MS"
                && RollbackPreferences.Milliseconds(30) == "250 MS"
                && RollbackPreferences.Milliseconds(119) == "992 MS",
            "rollback settings use three total digits in milliseconds"
        );
        var setup = new MatchSetup(7);
        check(setup.Preferences == new MatchPreferences(), "new sessions start with default match settings");
        setup.Preferences.TeamMode = true;
        setup.Preferences.WinScore = 12;
        setup.Preferences.MatchRounds = 4;
        setup.Preferences.FirstMap = 3;
        setup.Preferences.ShuffleMaps = true;
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
        check(match.Rules.WinScore == 12, "editing the lobby does not mutate active match rules");
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

        setup.ResetPreferences();
        check(setup.Preferences == new MatchPreferences(), "resetting the lobby clears every match preference");
        check(
            settings.Volume == .35f && settings.Keyboard[1].Strafe == Keys.N,
            "lobby reset leaves personal settings intact"
        );
        check(
            !JsonSerializer.Serialize(setup.CreateOptions()).Contains("CharactersBounceEachOther"),
            "match rules no longer contain the body-bounce modifier"
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
        check(MenuInput.Read(controls, 3) == default, "inactive controller has no menu actions");
        check(
            MenuInput.Read(controls).Accept && MenuInput.Read(controls).Vertical == 1 && controls.MousePressed,
            "keyboard and controller menu actions combine without doubling navigation"
        );
        pads[1] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A | Buttons.DPadRight);
        controls.Poll();
        check(
            MenuInput.Read(controls).Accept && MenuInput.Read(controls).Horizontal == 1 && !controls.MousePressed,
            "another controller can navigate while a held mouse does not repeat"
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
        check(MenuInput.Read(controls).Vertical == -1, "keyboard navigation remains available after controller input");
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
        mouse = new(
            200,
            200,
            0,
            ButtonState.Pressed,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released
        );
        controls.Poll(windowActive: false);
        check(
            !controls.MouseMoved && !controls.MousePressed && !controls.MouseRightPressed,
            "inactive mouse clicks and motion are ignored"
        );
        mouse = new(
            300,
            300,
            0,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released
        );
        controls.Poll(windowActive: false);
        mouse = new(
            400,
            400,
            0,
            ButtonState.Pressed,
            ButtonState.Released,
            ButtonState.Pressed,
            ButtonState.Released,
            ButtonState.Released
        );
        controls.Poll();
        check(
            !controls.MouseMoved && !controls.MousePressed && !controls.MouseRightPressed,
            "refocusing cannot click or hover a menu item"
        );
        controls.Poll();
        check(!controls.MousePressed && !controls.MouseRightPressed, "held focus click stays suppressed");
        mouse = new(
            400,
            400,
            0,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released
        );
        controls.Poll();
        mouse = new(
            410,
            410,
            0,
            ButtonState.Pressed,
            ButtonState.Released,
            ButtonState.Pressed,
            ButtonState.Released,
            ButtonState.Released
        );
        controls.Poll();
        check(
            controls.MouseMoved && controls.MousePressed && controls.MouseRightPressed,
            "fresh focused mouse input works normally"
        );
        Console.WriteLine("Settings validation, match isolation, local party and shared menu input passed");
    }
}
