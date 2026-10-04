using FrogSmashers.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class MenuRenderer(FrogGame game, MenuController menu)
{
    private static readonly Color Selected = new(255, 236, 164);
    private readonly Dictionary<string, Rectangle> glyphSources = new();
    private readonly float arrowFrameSeconds = game
        .Assets.Data.PresentationScenes["TitleScreen"]
        .Sprites.Single(sprite => sprite.ObjectPath == "Start")
        .FrameSeconds;
    private const float HintGap = 4;

    public MenuLayout.Measurement Measure(IReadOnlyList<MenuEntry> entries) =>
        MenuLayout.Measure(
            menu.Screen,
            entries,
            game.Assets.Font,
            menu.ShowStickInputs,
            menu.Selected,
            accessorySize: EntryGlyphSize
        );

    private Vector2 EntryGlyphSize(MenuEntry entry)
    {
        ButtonGlyph[] glyphs = entry.Key is { } key
            ? [ButtonGlyph.Key(key)]
            : ButtonGlyph.PadBinding(entry.Button!.Value);
        return new Vector2(
            glyphs.Sum(GlyphWidth) + HintGap * (glyphs.Length - 1),
            glyphs.Max(glyph => game.Renderer.ImageSize(GlyphSource(glyph), GlyphSource(glyph).Width * 2).Y)
        );
    }

    public void Draw()
    {
        if (menu.Screen is GameScreen.Intro or GameScreen.Title)
            return;
        if (menu.ShowingLobby)
            DrawLobby();
        if (menu.Screen == GameScreen.Outro)
        {
            if (game.Online.Lobby is { IsHost: false })
            {
                game.Renderer.CenteredText("WAITING FOR HOST", 640, 660, Color.White, center: true);
                CenteredHint(ButtonGlyph.Back(menu.HintDevice), "QUIT GAME", 640, 692);
            }
            else
                CenteredHint(ButtonGlyph.Accept(menu.HintDevice), "RETURN TO LOBBY", 640, 692);
            return;
        }
        if (menu.Screen == GameScreen.Seats)
            return;
        if (menu.Screen == GameScreen.Playing && !game.Match.Paused)
            return;
        if (menu.Screen == GameScreen.SlotEditor)
        {
            DrawSlotEditor();
            return;
        }
        var entries = menu.Entries();
        var layout = Measure(entries);
        var panel = layout.Panel;
        game.Renderer.MenuPanel(panel);
        if (menu.Screen == GameScreen.Main)
            game.Renderer.Image(
                "Textures/Sprites/logo_rebuilt",
                new Rectangle(50, 39, 160, 97),
                new Vector2(640, 22),
                480
            );
        else
        {
            bool bindings = menu.Screen == GameScreen.Bindings;
            game.Renderer.CenteredText(
                game.Assets.Font.Wrap(Title(), panel.Width - (bindings ? 104 : 40), 2),
                panel.Center.X,
                panel.Top + (bindings ? 48 : 36),
                bindings && menu.Selected == 0 ? Selected : Color.White,
                center: true
            );
            Separator(panel, MenuLayout.HeaderLine(menu.Screen, panel));
            if (bindings && menu.Selected == 0)
                DrawDeviceArrows(panel);
        }
        Separator(panel, MenuLayout.FooterLine(menu.Screen, panel));
        for (int i = 0; i < entries.Count; i++)
        {
            var rect = layout.Rows[i].Bounds;
            if (rect.IsEmpty)
                continue;
            var entry = entries[i];
            if (entry.SeparatorBefore && i != layout.PageStarts[layout.Page])
                Separator(panel, MenuLayout.SectionLine(rect));
            if (entry.IsTitle)
                continue;
            bool selected = i == menu.Selected;
            var color = entry.DisplayColor(selected);
            DrawEntry(entry, layout.Rows[i], panel, color);
            if (selected)
                DrawArrows(panel, rect.Center.Y);
        }
        if (menu.ShowStickInputs)
            DrawStickInputs(panel);
        DrawFooter(panel);
        if (layout.Paginated)
        {
            var previous = MenuLayout.PageButton(layout, -1);
            var next = MenuLayout.PageButton(layout, 1);
            game.Renderer.CenteredText(
                $"< {layout.Page + 1}/{layout.PageStarts.Length}",
                previous.Center.X,
                previous.Center.Y,
                Color.White,
                center: true
            );
            game.Renderer.CenteredText("NEXT >", next.Center.X, next.Center.Y, Color.White, center: true);
        }
        if (
            menu.Screen == GameScreen.Main
            && game.FirewallCheck.IsCompletedSuccessfully
            && game.FirewallCheck.Result == true
        )
        {
            var font = game.Assets.Font;
            string warning = font.Wrap(Ipv6FirewallCheck.Warning, Renderer.Width - 32);
            var size = font.Measure(warning);
            game.Renderer.Panel(
                new Rectangle(
                    (Renderer.Width - (int)size.X) / 2 - 8,
                    Renderer.Height - 22 - (int)size.Y,
                    (int)size.X + 16,
                    (int)size.Y + 12
                ),
                Color.Black * .8f
            );
            game.Renderer.Text(
                warning,
                Renderer.Width / 2,
                Renderer.Height - 16 - size.Y,
                new Color(255, 220, 130),
                center: true
            );
        }
    }

    private void DrawEntry(MenuEntry entry, MenuLayout.MeasuredRow measured, Rectangle panel, Color color)
    {
        var font = game.Assets.Font;
        var row = measured.Bounds;
        var text = measured.Text;
        if (menu.Screen == GameScreen.Main)
        {
            game.Renderer.CenteredText(text.Label, row.Center.X, row.Center.Y, color, center: true);
            return;
        }
        float left = panel.Left + 32;
        float right = panel.Right - 32;
        float width = right - left;
        if (entry.Key != null || entry.Button != null)
        {
            ButtonGlyph[] glyphs = entry.Key is { } key
                ? [ButtonGlyph.Key(key)]
                : ButtonGlyph.PadBinding(entry.Button!.Value);
            float glyphWidth = glyphs.Sum(GlyphWidth) + HintGap * (glyphs.Length - 1);
            string label = text.Label;
            game.Renderer.CenteredText(label, left, row.Center.Y, color);
            float x = right - glyphWidth;
            foreach (var glyph in glyphs)
            {
                Glyph(glyph, x, row.Center.Y);
                x += GlyphWidth(glyph) + HintGap;
            }
        }
        else if (text.Value is { } value)
        {
            if (!text.Stacked)
            {
                game.Renderer.CenteredText(text.Label, left, row.Center.Y, color);
                RightText(value, right, row.Center.Y, color);
            }
            else
            {
                float top = row.Center.Y - text.Height / 2;
                game.Renderer.Text(text.Label, left, top, color);
                RightText(value, right, top + text.Height - font.Measure(value).Y / 2, color);
            }
        }
        else
            game.Renderer.CenteredText(text.Label, left, row.Center.Y, color);
    }

    private void RightText(string text, float right, float centerY, Color color)
    {
        var font = game.Assets.Font;
        float top = centerY - font.Measure(text).Y / 2;
        float lineHeight = font.Measure("H\nH").Y - font.Measure("H").Y;
        foreach (string line in text.Split('\n'))
        {
            game.Renderer.Text(line, right - font.Measure(line).X, top, color);
            top += lineHeight;
        }
    }

    private void DrawArrows(Rectangle panel, float centerY)
    {
        var texture = game.Assets.Texture("UI/whitearrow");
        var scale = game.Assets.Font.PixelScale(1);
        bool outward = (long)(menu.AnimationTime / arrowFrameSeconds) % 2 != 0;
        float inset = outward ? 7 : 8;
        float panelLeft = MathF.Round(panel.Left / scale.X) * scale.X;
        float panelRight = panelLeft + MathF.Round(panel.Width / scale.X) * scale.X;
        float left = panelLeft + inset * scale.X;
        float right = panelRight - (inset + texture.Width) * scale.X;
        float top = MathF.Round((centerY - texture.Height * scale.Y / 2) / scale.Y) * scale.Y;
        void Draw(float x, SpriteEffects flip) =>
            game.Renderer.Batch.Draw(
                texture,
                new Vector2(MathF.Round(x / scale.X) * scale.X, top),
                null,
                Color.White,
                0,
                Vector2.Zero,
                scale,
                flip,
                0
            );
        Draw(left, SpriteEffects.FlipHorizontally);
        Draw(right, SpriteEffects.None);
    }

    private void Separator(Rectangle panel, float y)
    {
        var scale = game.Assets.Font.PixelScale(1);
        float left = MathF.Round((panel.Left + 18) / scale.X) * scale.X;
        float right = MathF.Round((panel.Right - 18) / scale.X) * scale.X;
        float top = MathF.Round(y / scale.Y) * scale.Y;
        game.Renderer.Batch.Draw(
            game.Assets.White,
            new Vector2(left, top),
            new Rectangle(0, 0, 1, 1),
            Color.White,
            0,
            Vector2.Zero,
            new Vector2(right - left, scale.Y),
            SpriteEffects.None,
            0
        );
    }

    private void DrawDeviceArrows(Rectangle panel)
    {
        var texture = game.Assets.Texture("UI/whitearrow");
        var scale = game.Assets.Font.PixelScale(1);
        bool outward = (long)(menu.AnimationTime / arrowFrameSeconds) % 2 != 0;
        foreach (int direction in new[] { -1, 1 })
        {
            var button = MenuLayout.BindingPageButton(panel, direction);
            var position = new Vector2(
                MathF.Round((button.Center.X - texture.Width * scale.X / 2) / scale.X) * scale.X
                    + (outward ? direction * scale.X : 0),
                MathF.Round((button.Center.Y - texture.Height * scale.Y / 2) / scale.Y) * scale.Y
            );
            game.Renderer.Batch.Draw(
                texture,
                position,
                null,
                Color.White,
                0,
                Vector2.Zero,
                scale,
                direction < 0 ? SpriteEffects.None : SpriteEffects.FlipHorizontally,
                0
            );
        }
    }

    private void DrawStickInputs(Rectangle panel)
    {
        var pad = game.Controls.Pads[menu.BindingDevice - 2];
        var scale = game.Assets.Font.PixelScale(1);
        DrawStick(pad.ThumbSticks.Left, panel.Center.X - 48, "leftStick");
        DrawStick(pad.ThumbSticks.Right, panel.Center.X + 64, "rightStick");

        void DrawStick(Vector2 value, float x, string icon)
        {
            var center = new Vector2(
                MathF.Round(x / scale.X) * scale.X,
                MathF.Round((panel.Bottom - 76) / scale.Y) * scale.Y
            );
            Glyph(ButtonGlyph.Pad(icon), center.X - 40, center.Y);
            for (int y = -6; y <= 6; y++)
            for (int column = -6; column <= 6; column++)
            {
                int distance = column * column + y * y;
                if (distance is >= 25 and <= 36)
                    Pixel(center + new Vector2(column, y) * scale, new Color(135, 145, 136));
            }
            var dot = new Vector2(MathF.Round(value.X * 5), -MathF.Round(value.Y * 5));
            Pixel(center + dot * scale, Color.White);
        }

        void Pixel(Vector2 position, Color color) =>
            game.Renderer.Batch.Draw(
                game.Assets.White,
                position,
                new Rectangle(0, 0, 1, 1),
                color,
                0,
                Vector2.Zero,
                scale,
                SpriteEffects.None,
                0
            );
    }

    private void DrawFooter(Rectangle panel)
    {
        if (menu.Screen == GameScreen.Modifiers)
        {
            var entry = menu.Entries()[menu.Selected];
            string help = entry.Help ?? "RETURN TO THE LOBBY MENU.";
            if (entry.DisabledReason != null)
                help = entry.DisabledReason + ". " + help;
            game.Renderer.Text(
                game.Assets.Font.Wrap(help, panel.Width - 40, 3),
                panel.Center.X,
                panel.Bottom - 128,
                new Color(205, 218, 206),
                center: true
            );
        }
        if (menu.Screen == GameScreen.ViewPlayers)
        {
            DrawPlayerActions(panel);
            return;
        }
        if (menu.WaitingForBinding)
        {
            CenteredHint(ButtonGlyph.Menu(menu.HintDevice), "CANCEL", panel.Center.X, panel.Bottom - 30);
            return;
        }
        var accept = ButtonGlyph.Accept(menu.HintDevice);
        if (menu.Screen == GameScreen.Main)
        {
            CenteredHint(accept, "SELECT", panel.Center.X, panel.Bottom - 30);
            return;
        }
        var back = ButtonGlyph.Back(menu.HintDevice);
        string acceptText = menu.EditingAddress ? "DONE" : "SELECT";
        string backText =
            menu.WaitingForBinding ? "CANCEL"
            : menu.EditingAddress ? "DONE"
            : "BACK";
        float acceptWidth = HintWidth(accept, acceptText);
        float backWidth = HintWidth(back, backText);
        float gap = Math.Clamp(panel.Width - 40 - acceptWidth - backWidth, 8, 32);
        float left = panel.Center.X - (acceptWidth + gap + backWidth) / 2;
        ActionHint(accept, acceptText, left, panel.Bottom - 30);
        ActionHint(back, backText, left + acceptWidth + gap, panel.Bottom - 30);
    }

    private void DrawPlayerActions(Rectangle panel)
    {
        var actions = menu.PlayerActions();
        bool paired = actions.Accept != null && actions.Remove != null;
        void Draw(MenuEntry entry, ButtonGlyph glyph, Rectangle rect) =>
            CenteredHint(glyph, entry.Label, rect.Center.X, rect.Center.Y, entry.DisplayColor(false));
        if (actions.Accept != null)
            Draw(actions.Accept, ButtonGlyph.Accept(menu.HintDevice), MenuLayout.PlayerAction(panel, false, paired));
        if (actions.Remove != null)
            Draw(actions.Remove, ButtonGlyph.Remove(menu.HintDevice), MenuLayout.PlayerAction(panel, true, paired));
        var back = MenuLayout.PlayerBack(panel, paired);
        CenteredHint(ButtonGlyph.Back(menu.HintDevice), "BACK", back.Center.X, back.Center.Y);
    }

    private void DrawSlotEditor()
    {
        game.Renderer.DrawRoomOutline(menu.SelectedSeat);
        var room = MenuLayout.RoomInterior(menu.SelectedSeat);
        var slot = game.Lobby.Roster.Slots[menu.SelectedSeat];
        game.Renderer.CenteredText(
            MenuController.SlotLabel(menu.SelectedSlotType),
            room.Center.X,
            room.Top + 28,
            Color.White,
            center: true
        );
        if (game.Lobby.SlotEditPending(menu.SelectedSeat))
            game.Renderer.CenteredText(
                "APPLYING",
                room.Center.X,
                room.Top + 56,
                new Color(180, 190, 180),
                center: true
            );
        if (slot.Player is not { Cpu: false })
            CenteredHint(
                ButtonGlyph.Accept(menu.HintDevice),
                "CHANGE TYPE",
                room.Center.X,
                MenuLayout.SlotAction(menu.SelectedSeat, 0).Center.Y
            );
        if (slot.Player is { } player)
            CenteredHint(
                ButtonGlyph.Remove(menu.HintDevice),
                game.Lobby.RemoveLabel(player),
                room.Center.X,
                MenuLayout.SlotAction(menu.SelectedSeat, 1).Center.Y
            );
        CenteredHint(
            ButtonGlyph.ApplyAll(menu.HintDevice),
            "APPLY TO ALL",
            room.Center.X,
            MenuLayout.SlotAction(menu.SelectedSeat, 2).Center.Y
        );
        ActionHint(ButtonGlyph.Back(menu.HintDevice), "BACK", MenuLayout.SlotBack.Left, MenuLayout.SlotBack.Center.Y);
    }

    private string Title() =>
        menu.Screen switch
        {
            GameScreen.LobbyMenu => "LOBBY",
            GameScreen.Settings => "SETTINGS",
            GameScreen.Rollback => "ROLLBACK",
            GameScreen.MatchSettings => "MATCH SETTINGS",
            GameScreen.Modifiers => "MODIFIERS",
            GameScreen.Bindings => menu.BindingTitle,
            GameScreen.Online => "ONLINE OPTIONS",
            GameScreen.CreateLobby => "CREATE LOBBY",
            GameScreen.CreateLobbyAdvanced => "ADVANCED",
            GameScreen.JoinLobby => "JOIN LOBBY",
            GameScreen.ViewPlayers => "PLAYERS",
            GameScreen.BrowseSteam => "STEAM LOBBIES",
            GameScreen.BrowseLan => "LAN LOBBIES",
            GameScreen.JoinUdp => "JOIN BY ADDRESS",
            GameScreen.Extras => "EXTRAS",
            GameScreen.Connecting => "CONNECTING",
            GameScreen.Playing => game.Match.Network == null ? "PAUSED" : "MATCH CONTINUES",
            _ => "",
        };

    private void DrawLobby()
    {
        bool editing = menu.Screen == GameScreen.SlotEditor;
        string heading =
            game.Online.Lobby is SteamLobby steam ? $"STEAM LOBBY {steam.LobbyCode}"
            : game.Online.Lobby != null ? $"UDP LOBBY :{game.Options.Port}"
            : "LOCAL LOBBY";
        if (!editing)
            game.Renderer.Text(heading, 640, 17, Color.White, center: true);
        if (!editing && game.Lobby.ProgressText is { } progress)
            game.Renderer.Text(progress, 640, 44, Color.White, center: true);
        var joinDevices = game.Lobby.JoinHintDevices();
        int joinHint = 0;
        for (int room = 0; room < 8; room++)
        {
            var rect = MenuLayout.RoomInterior(room);
            var slot = game.Lobby.Presentation.Roster.Slots[room];
            var player = slot.Player;
            if (!editing && game.Lobby.Presentation.Status(room) is { } pending)
            {
                game.Renderer.CenteredText(pending, rect.Center.X, rect.Top + 24, Color.Black, center: true);
                continue;
            }
            if (player == null || editing)
            {
                game.Renderer.CenteredText(
                    MenuController.SlotLabel(game.Lobby.GetSlotType(room)),
                    rect.Center.X,
                    rect.Top + 28,
                    Color.Black,
                    center: true
                );
                if (
                    !editing
                    && menu.Screen == GameScreen.Seats
                    && player == null
                    && (game.Lobby.Online?.Access ?? default).Allows(slot.Type, game.Lobby.LocalPeer)
                    && joinHint < joinDevices.Length
                )
                    PressHint(LobbyJoinGlyph(joinDevices[joinHint++]), rect.Center.X, rect.Bottom - 46);
                continue;
            }
            string label =
                player.Cpu ? "CPU"
                : player.Peer == game.Lobby.LocalPeer ? new LocalSeat(player.Id).Label
                : $"PLAYER {room + 1}";
            if (game.Lobby.UsesTeams)
                label += $" / TEAM {player.Team + 1}";
            game.Renderer.CenteredText(label, rect.Center.X, rect.Top + 24, Color.Black, center: true);
            if (player.Cpu || menu.Screen != GameScreen.Seats || player.Peer != game.Lobby.LocalPeer)
                continue;
            if (!player.Spawned)
            {
                var colorButton =
                    player.Id >= 2
                        ? ButtonGlyph.PadBinding(game.Controls.ControllerBindings(player.Id - 2).Tongue)[0]
                        : ButtonGlyph.Key(game.Settings.Keyboard[player.Id].Tongue);
                CenteredHint(
                    colorButton,
                    game.Lobby.UsesTeams ? $"COLOR / TEAM {player.Team + 1}" : "COLOR",
                    rect.Center.X,
                    rect.Bottom - 72,
                    Color.Black
                );
                CenteredHint(LobbyJoinGlyph(player.Id), "SPAWN", rect.Center.X, rect.Bottom - 46, Color.Black);
                var backButton =
                    player.Id >= 2
                        ? ButtonGlyph.PadBinding(game.Controls.ControllerBindings(player.Id - 2).Jump)[0]
                        : ButtonGlyph.Key(game.Settings.Keyboard[player.Id].Jump);
                CenteredHint(
                    backButton,
                    game.Lobby.Online is { IsHost: false } && game.Lobby.Roster.Humans(player.Peer).Length == 1
                        ? "SPECTATE"
                        : "BACK OUT",
                    rect.Center.X,
                    rect.Bottom - 20,
                    Color.Black
                );
            }
            else if (game.Lobby.CanChooseAgain(room))
                CenteredHint(
                    LobbyJoinGlyph(player.Id),
                    game.Lobby.UsesTeams ? "COLOR / TEAM" : "CHANGE COLOR",
                    rect.Center.X,
                    rect.Bottom - 46,
                    Color.Black
                );
        }
    }

    private ButtonGlyph LobbyJoinGlyph(int device) =>
        device >= 2
            ? ButtonGlyph.PadBinding(game.Controls.ControllerBindings(device - 2).Attack)[0]
            : ButtonGlyph.Key(game.Settings.Keyboard[device].Attack);

    private void PressHint(ButtonGlyph glyph, float x, float y)
    {
        float textWidth = game.Assets.Font.Measure("PRESS").X;
        float left = x - (textWidth + HintGap + GlyphWidth(glyph)) / 2;
        game.Renderer.CenteredText("PRESS", left, y, Color.Black);
        Glyph(glyph, left + textWidth + HintGap, y);
    }

    private Rectangle GlyphSource(ButtonGlyph glyph)
    {
        if (glyphSources.TryGetValue(glyph.Path, out var source))
            return source;
        var texture = game.Assets.Texture(glyph.Path);
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        int left = texture.Width,
            top = texture.Height,
            right = 0,
            bottom = 0;
        for (int y = 0; y < texture.Height; y++)
        for (int x = 0; x < texture.Width; x++)
            if (pixels[y * texture.Width + x].A > 0)
            {
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        source = new Rectangle(left, top, right - left, bottom - top);
        glyphSources.Add(glyph.Path, source);
        return source;
    }

    private float GlyphWidth(ButtonGlyph glyph)
    {
        var source = GlyphSource(glyph);
        return game.Renderer.ImageSize(source, source.Width * 2).X;
    }

    private float HintWidth(ButtonGlyph glyph, string text) =>
        GlyphWidth(glyph) + HintGap + game.Assets.Font.Measure(text).X;

    private void CenteredHint(ButtonGlyph glyph, string text, float x, float y, Color? color = null) =>
        ActionHint(glyph, text, x - HintWidth(glyph, text) / 2, y, color);

    private void ActionHint(ButtonGlyph glyph, string text, float x, float y, Color? color = null)
    {
        Glyph(glyph, x, y);
        game.Renderer.CenteredText(text, x + GlyphWidth(glyph) + HintGap, y, color ?? Color.White);
    }

    private void Glyph(ButtonGlyph glyph, float x, float y)
    {
        var source = GlyphSource(glyph);
        float width = source.Width * 2;
        var size = game.Renderer.ImageSize(source, width);
        game.Renderer.Image(glyph.Path, source, new Vector2(x + size.X / 2, y - size.Y / 2), width);
    }
}
