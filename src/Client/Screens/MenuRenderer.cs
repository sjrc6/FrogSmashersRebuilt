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
        var entries = menu.Entries();
        bool editingSlots = menu.Screen is GameScreen.SlotEditor or GameScreen.SlotOptions;
        var panel = MenuLayout.Panel(menu.Screen, entries.Count);
        if (editingSlots)
            DrawSlotEditor();
        else
        {
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
                    Color.White,
                    center: true
                );
                Separator(panel, panel.Top + (bindings ? 84 : 60));
                if (bindings)
                    DrawDeviceArrows(panel);
            }
            Separator(panel, panel.Bottom - 50);
        }
        for (int i = 0; i < entries.Count; i++)
        {
            var rect = Row(i, entries.Count);
            var entry = entries[i];
            bool selected = i == menu.Selected;
            var color = entry.Color ?? Color.White;
            if (entry.DisabledReason != null)
                color = new Color(135, 145, 136);
            else if (selected)
                color = entry.Color.HasValue ? Color.Lerp(color, Color.White, .55f) : Selected;
            DrawEntry(entry, rect, panel, color, editingSlots);
            if (selected && !editingSlots)
                DrawArrows(panel, rect.Center.Y);
        }
        if (editingSlots)
            return;
        if (menu.Screen == GameScreen.Bindings && menu.BindingDevice >= 2)
            DrawStickInputs(panel);
        DrawFooter(panel);
    }

    private void DrawEntry(MenuEntry entry, Rectangle row, Rectangle panel, Color color, bool editingSlots)
    {
        var font = game.Assets.Font;
        if (editingSlots || menu.Screen == GameScreen.Main)
        {
            string text = font.Wrap(entry.Text, editingSlots ? row.Width - 48 : panel.Width - 64, editingSlots ? 1 : 2);
            game.Renderer.CenteredText(text, row.Center.X, row.Center.Y, color, center: true);
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
            string label = font.Wrap(entry.Label, width - glyphWidth - 16, 2);
            game.Renderer.CenteredText(label, left, row.Center.Y, color);
            float x = right - glyphWidth;
            foreach (var glyph in glyphs)
            {
                Glyph(glyph, x, row.Center.Y);
                x += GlyphWidth(glyph) + HintGap;
            }
        }
        else if (entry.Value is { } value)
        {
            float valueWidth = font.Measure(value).X;
            if (valueWidth <= width * .55f)
            {
                string label = font.Wrap(entry.Label, width - valueWidth - 16, 2);
                game.Renderer.CenteredText(label, left, row.Center.Y, color);
                RightText(value, right, row.Center.Y, color);
            }
            else
            {
                string label = font.Wrap(entry.Label, width, 1);
                value = font.Wrap(value, width, 2);
                float labelHeight = font.Measure(label).Y;
                float valueHeight = font.Measure(value).Y;
                float top = row.Center.Y - (labelHeight + 6 + valueHeight) / 2;
                game.Renderer.Text(label, left, top, color);
                RightText(value, right, top + labelHeight + 6 + valueHeight / 2, color);
            }
        }
        else
            game.Renderer.CenteredText(font.Wrap(entry.Label, width, 2), left, row.Center.Y, color);
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
        foreach (int direction in new[] { -1, 1 })
        {
            var button = MenuLayout.BindingPageButton(panel, direction);
            var position = new Vector2(
                MathF.Round((button.Center.X - texture.Width * scale.X / 2) / scale.X) * scale.X,
                MathF.Round((button.Center.Y - texture.Height * scale.Y / 2) / scale.Y) * scale.Y
            );
            game.Renderer.Batch.Draw(
                texture,
                position,
                null,
                menu.WaitingForBinding ? Color.Gray : Color.White,
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

    private Rectangle Row(int index, int count) => MenuLayout.Row(menu.Screen, index, count, menu.SelectedSeat);

    private void DrawSlotEditor()
    {
        game.Renderer.DrawRoomOutline(menu.SelectedSeat);
        var room = MenuLayout.RoomInterior(menu.SelectedSeat);
        if (menu.Screen == GameScreen.SlotEditor)
            CenteredHint(ButtonGlyph.Accept(menu.HintDevice), "EDIT SLOT", room.Center.X, room.Top + 25);
        else
        {
            game.Renderer.CenteredText(
                $"SLOT {menu.SelectedSeat + 1}",
                room.Center.X,
                room.Top + 24,
                Color.White,
                1,
                true
            );
            ActionHint(ButtonGlyph.Accept(menu.HintDevice), "SELECT", room.Left + 14, room.Bottom - 19);
        }
        var back = ButtonGlyph.Back(menu.HintDevice);
        ActionHint(back, "BACK", room.Right - HintWidth(back, "BACK") - 14, room.Bottom - 19);
    }

    private string Title() =>
        menu.Screen switch
        {
            GameScreen.LobbyMenu => "LOBBY",
            GameScreen.Settings => "SETTINGS",
            GameScreen.MatchSettings => "MATCH SETTINGS",
            GameScreen.Bindings => menu.BindingTitle,
            GameScreen.Online => "ONLINE OPTIONS",
            GameScreen.HostSteam => "HOST STEAM",
            GameScreen.JoinSteam => "JOIN STEAM",
            GameScreen.Udp => "UDP / LAN",
            GameScreen.HostUdp => "HOST UDP",
            GameScreen.JoinUdp => "JOIN BY ADDRESS",
            GameScreen.Extras => "EXTRAS",
            GameScreen.Connecting => "CONNECTING",
            GameScreen.Playing => game.Match.Network == null ? "PAUSED" : "MATCH CONTINUES",
            _ => "",
        };

    private void DrawLobby()
    {
        if (menu.Screen is GameScreen.SlotEditor or GameScreen.SlotOptions)
            return;
        string heading =
            game.Online.Lobby is SteamLobby steam ? $"STEAM LOBBY {steam.LobbyCode}"
            : game.Online.Lobby != null ? $"UDP LOBBY :{game.Options.Port}"
            : "LOCAL LOBBY";
        game.Renderer.Text(heading, 640, 17, Color.White, center: true);
        for (int room = 0; room < 8; room++)
        {
            var rect = MenuLayout.RoomInterior(room);
            var slot = game.Lobby.Roster.Slots[room];
            var player = slot.Player;
            if (player == null)
            {
                if (slot.Open && game.Renderer.LobbyJoinPrompt && menu.Screen == GameScreen.Seats)
                {
                    var start = ButtonGlyph.Start(menu.HintDevice);
                    float textWidth = game.Assets.Font.Measure("PRESS").X;
                    float left = rect.Center.X - (textWidth + HintGap + GlyphWidth(start)) / 2;
                    game.Renderer.CenteredText("PRESS", left, rect.Top + 28, Color.Black);
                    Glyph(start, left + textWidth + HintGap, rect.Top + 28);
                }
                else
                    game.Renderer.CenteredText(
                        slot.Open ? "OPEN" : "CLOSED",
                        rect.Center.X,
                        rect.Top + 28,
                        Color.Black,
                        1,
                        true
                    );
                continue;
            }
            string label =
                player.Cpu ? "CPU"
                : player.Peer == game.Lobby.LocalPeer ? new LocalSeat(player.Id).Label
                : $"PLAYER {room + 1}";
            game.Renderer.CenteredText(label, rect.Center.X, rect.Top + 24, Color.Black, center: true);
            if (player.Cpu || menu.Screen != GameScreen.Seats || player.Peer != game.Lobby.LocalPeer)
                continue;
            if (!player.Spawned)
            {
                var colorButton =
                    player.Id >= 2
                        ? ButtonGlyph.PadBinding(game.Settings.Controllers[player.Id - 2].Tongue)[0]
                        : ButtonGlyph.Key(game.Settings.Keyboard[player.Id].Tongue);
                CenteredHint(
                    colorButton,
                    game.Lobby.TeamMode ? $"COLOR / TEAM {player.Team + 1}" : "COLOR",
                    rect.Center.X,
                    rect.Bottom - 72,
                    Color.Black
                );
                CenteredHint(ButtonGlyph.Start(player.Id), "SPAWN", rect.Center.X, rect.Bottom - 46, Color.Black);
                var backButton =
                    player.Id >= 2
                        ? ButtonGlyph.PadBinding(game.Settings.Controllers[player.Id - 2].Attack)[0]
                        : ButtonGlyph.Key(game.Settings.Keyboard[player.Id].Attack);
                CenteredHint(backButton, "BACK OUT", rect.Center.X, rect.Bottom - 20, Color.Black);
            }
            else if (game.Lobby.CanChooseAgain(room))
                CenteredHint(
                    ButtonGlyph.Start(player.Id),
                    game.Lobby.TeamMode ? "COLOR / TEAM" : "CHANGE COLOR",
                    rect.Center.X,
                    rect.Bottom - 46,
                    Color.Black
                );
        }
        if (menu.Screen != GameScreen.Seats)
            return;
        ActionHint(ButtonGlyph.Start(0), "JOIN P1", 65, 694);
        ActionHint(ButtonGlyph.Start(1), "JOIN P2", 270, 694);
        ActionHint(ButtonGlyph.Pad("startButton"), "JOIN / MENU", 475, 694);
        ActionHint(ButtonGlyph.Menu(0), "MENU", 1020, 694);
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
