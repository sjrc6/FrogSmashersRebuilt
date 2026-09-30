using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class MenuRenderer(FrogGame game, MenuController menu)
{
    private static readonly Color Selected = new(255, 236, 164);
    private static readonly Color PanelColor = new(25, 36, 40, 178);
    private readonly Dictionary<string, Rectangle> glyphSources = new();
    private const float HintScale = .85f;
    private const float HintGap = 4;

    public void Draw()
    {
        if (menu.Screen is GameScreen.Intro or GameScreen.Title)
            return;
        if (menu.ShowingLobby)
            DrawLobby();
        if (menu.Screen == GameScreen.Outro)
        {
            ActionHint(ButtonGlyph.Accept(menu.HintDevice), "RETURN TO LOBBY", 530, 692);
            return;
        }
        if (menu.Screen == GameScreen.Seats)
        {
            DrawStatus();
            return;
        }
        if (menu.Screen == GameScreen.Playing && !game.Match.Paused)
            return;
        var entries = menu.Entries();
        bool editingSlots = menu.Screen is GameScreen.SlotEditor or GameScreen.SlotOptions;
        if (editingSlots)
            DrawSlotEditor();
        else if (menu.Screen == GameScreen.Main)
            game.Renderer.Image(
                "Textures/Sprites/logo_rebuilt",
                new Rectangle(50, 39, 160, 97),
                new Vector2(640, 22),
                480
            );
        else
        {
            var first = Row(0, entries.Count);
            var last = Row(Math.Max(0, entries.Count - 1), entries.Count);
            game.Renderer.Panel(
                new Rectangle(first.X - 12, first.Y - 47, first.Width + 24, last.Bottom - first.Y + 60),
                PanelColor
            );
            game.Renderer.CenteredText(Title(), 640, first.Y - 22, Color.White, 1.1f, true);
        }
        if (menu.Screen == GameScreen.Connecting)
            game.Renderer.Text(game.Online.Lobby?.Status ?? "", 640, 283, Color.White, 1, true);
        if (menu.Screen == GameScreen.Error)
        {
            game.Renderer.Panel(new Rectangle(120, 190, 1040, 290), PanelColor);
            DrawWrapped(menu.Status, 155, 218, 970, Color.White);
        }
        for (int i = 0; i < entries.Count; i++)
        {
            var rect = Row(i, entries.Count);
            var entry = entries[i];
            bool selected = i == menu.Selected;
            game.Renderer.Panel(rect, selected ? new Color(99, 117, 120, 195) : new Color(22, 32, 38, 105));
            float scale = Math.Min(
                editingSlots ? .82f : .86f,
                (rect.Width - 24) / Math.Max(1, game.Assets.Font.Measure(entry.Label, 1).X)
            );
            var color = entry.Color ?? (selected ? Selected : Color.White);
            if (entry.Key is { } key)
            {
                game.Renderer.CenteredText(entry.Label, rect.X + 26, rect.Center.Y, color, scale);
                var glyph = ButtonGlyph.Key(key);
                Glyph(glyph, rect.Right - 26 - GlyphWidth(glyph), rect.Center.Y);
            }
            else
                game.Renderer.CenteredText(entry.Label, rect.Center.X, rect.Center.Y, color, scale, true);
        }
        if (editingSlots)
            return;
        DrawStatus();
        if (menu.WaitingForBinding)
            game.Renderer.Text("PRESS A KEY TO BIND", 640, 645, Color.White, .8f, true);
        if (menu.EditingAddress)
            game.Renderer.Text("TYPE ADDRESS / LOBBY ID", 640, 645, Color.White, .8f, true);
        ActionHint(ButtonGlyph.Accept(menu.HintDevice), menu.EditingAddress ? "DONE" : "SELECT", 490, 692);
        ActionHint(
            ButtonGlyph.Back(menu.HintDevice),
            menu.WaitingForBinding || menu.EditingAddress ? "CANCEL" : "BACK",
            680,
            692
        );
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
                .95f,
                true
            );
            ActionHint(ButtonGlyph.Accept(menu.HintDevice), "SELECT", room.Left + 14, room.Bottom - 19);
        }
        var back = ButtonGlyph.Back(menu.HintDevice);
        ActionHint(back, "BACK", room.Right - HintWidth(back, "BACK") - 14, room.Bottom - 19);
        if (menu.Status.Length > 0)
            game.Renderer.CenteredText(menu.Status, room.Center.X, room.Center.Y, Selected, .6f, true);
    }

    private string Title() =>
        menu.Screen switch
        {
            GameScreen.LobbyMenu => "LOBBY MENU",
            GameScreen.Settings => "SETTINGS",
            GameScreen.MatchSettings => "MATCH SETTINGS",
            GameScreen.Bindings => $"KEYBOARD {menu.BindingDevice + 1} CONTROLS",
            GameScreen.Online => "ONLINE OPTIONS",
            GameScreen.HostSteam => "HOST STEAM LOBBY",
            GameScreen.JoinSteam => "JOIN STEAM LOBBY",
            GameScreen.Udp => "UDP / LAN",
            GameScreen.HostUdp => "HOST UDP / LAN",
            GameScreen.JoinUdp => "JOIN BY ADDRESS",
            GameScreen.Extras => "EXTRAS",
            GameScreen.Connecting => "CONNECTING",
            GameScreen.Error => "COULD NOT CONTINUE",
            GameScreen.Playing => game.Match.Network == null ? "PAUSED" : "MATCH CONTINUES",
            _ => "",
        };

    private void DrawLobby()
    {
        if (menu.Screen is GameScreen.SlotEditor or GameScreen.SlotOptions)
            return;
        string heading =
            game.Online.Lobby is SteamLobby steam ? $"STEAM LOBBY {steam.LobbyCode}"
            : game.Online.Lobby != null ? $"UDP LOBBY PORT {game.Options.Port}"
            : "LOCAL LOBBY";
        game.Renderer.Text(heading, 640, 17, Color.White, .9f, true);
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
                    float textWidth = game.Assets.Font.Measure("PRESS", HintScale).X;
                    float left = rect.Center.X - (textWidth + HintGap + GlyphWidth(start)) / 2;
                    game.Renderer.CenteredText("PRESS", left, rect.Top + 28, Color.Black, HintScale);
                    Glyph(start, left + textWidth + HintGap, rect.Top + 28);
                }
                else
                    game.Renderer.CenteredText(
                        slot.Open ? "OPEN" : "CLOSED",
                        rect.Center.X,
                        rect.Top + 28,
                        Color.Black,
                        HintScale,
                        true
                    );
                continue;
            }
            string label =
                player.Cpu ? "CPU"
                : player.Peer == game.Lobby.LocalPeer ? new LocalSeat(player.Id).Label
                : $"PLAYER {room + 1}";
            game.Renderer.CenteredText(label, rect.Center.X, rect.Top + 24, Color.Black, .8f, true);
            if (!player.Spawned && menu.Screen == GameScreen.Seats && player.Peer == game.Lobby.LocalPeer)
            {
                var colorButton =
                    player.Id >= 2
                        ? ButtonGlyph.Pad("aButton")
                        : ButtonGlyph.Key(game.Settings.Keyboard[player.Id].Tongue);
                CenteredHint(
                    colorButton,
                    game.Lobby.TeamMode ? $"COLOR / TEAM {player.Team + 1}" : "COLOR",
                    rect.Center.X,
                    rect.Bottom - 60,
                    Color.Black
                );
                CenteredHint(ButtonGlyph.Start(player.Id), "SPAWN", rect.Center.X, rect.Bottom - 24, Color.Black);
            }
        }
        if (menu.Screen != GameScreen.Seats)
            return;
        ActionHint(ButtonGlyph.Start(0), "JOIN P1", 65, 694);
        ActionHint(ButtonGlyph.Start(1), "JOIN P2", 270, 694);
        ActionHint(ButtonGlyph.Pad("startButton"), "JOIN / MENU", 475, 694);
        ActionHint(ButtonGlyph.Menu(0), "MENU", 1020, 694);
    }

    private void DrawStatus()
    {
        string status = menu.Status.Length > 0 ? menu.Status : game.Online.Lobby?.Notice ?? "";
        if (status.Length > 0 && menu.Screen != GameScreen.Error)
            game.Renderer.Text(status, 640, 645, Selected, .65f, true);
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
        GlyphWidth(glyph) + HintGap + game.Assets.Font.Measure(text, HintScale).X;

    private void CenteredHint(ButtonGlyph glyph, string text, float x, float y, Color? color = null) =>
        ActionHint(glyph, text, x - HintWidth(glyph, text) / 2, y, color);

    private void ActionHint(ButtonGlyph glyph, string text, float x, float y, Color? color = null)
    {
        Glyph(glyph, x, y);
        game.Renderer.CenteredText(text, x + GlyphWidth(glyph) + HintGap, y, color ?? Color.White, HintScale);
    }

    private void Glyph(ButtonGlyph glyph, float x, float y)
    {
        var source = GlyphSource(glyph);
        float width = source.Width * 2;
        var size = game.Renderer.ImageSize(source, width);
        game.Renderer.Image(glyph.Path, source, new Vector2(x + size.X / 2, y + 1 - size.Y / 2), width);
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
