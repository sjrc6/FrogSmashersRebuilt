using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class MenuLayout
{
    private const int RowPadding = 7;
    private const int LineThickness = 2;
    private const int SectionSpacing = RowPadding * 2 + LineThickness;

    public sealed record Measurement(Rectangle Panel, MeasuredRow[] Rows);

    public readonly record struct MeasuredRow(Rectangle Bounds, MenuText Text);

    public static Measurement Measure(
        GameScreen screen,
        IReadOnlyList<MenuEntry> entries,
        BitmapFont font,
        bool showSticks = false
    )
    {
        int width = PanelWidth(screen);
        var rows = new MeasuredRow[entries.Count];
        int offset = RowsOffset(screen);
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var text = Text(entry, width, font);
            if (entry.SeparatorBefore)
                offset += SectionSpacing;
            int rowHeight = (int)MathF.Ceiling(text.Height) + RowPadding * 2;
            var bounds = entry.IsTitle
                ? new Rectangle(12, 12, width - 24, 72)
                : new Rectangle(12, offset, width - 24, rowHeight);
            rows[index] = new(bounds, text);
            if (!entry.IsTitle)
                offset += rowHeight;
        }
        int height = offset + (showSticks ? 32 : 0) + RowPadding + FooterHeight(screen);
        int top = screen == GameScreen.Main ? 324 : (Renderer.Height - height) / 4 * 2;
        var panel = new Rectangle((Renderer.Width - width) / 2, top, width, height);
        for (int index = 0; index < rows.Length; index++)
        {
            var bounds = rows[index].Bounds;
            bounds.Offset(panel.Location);
            rows[index] = rows[index] with { Bounds = bounds };
        }
        return new(panel, rows);
    }

    private static int PanelWidth(GameScreen screen) =>
        screen switch
        {
            GameScreen.Main => 240,
            GameScreen.Bindings => 440,
            GameScreen.BrowseSteam or GameScreen.BrowseLan => 520,
            _ => 400,
        };

    private static MenuText Text(MenuEntry entry, int panelWidth, BitmapFont font)
    {
        float width = panelWidth - 64;
        string label;
        if (entry.Value is not { } value)
        {
            label = font.Wrap(entry.Label, width, 2);
            return new(label, null, false, font.Measure(label).Y);
        }
        float valueWidth = font.Measure(value).X;
        if (valueWidth <= width * .55f)
        {
            label = font.Wrap(entry.Label, width - valueWidth - 16, 2);
            return new(label, value, false, Math.Max(font.Measure(label).Y, font.Measure(value).Y));
        }
        label = font.Wrap(entry.Label, width, 1);
        value = font.Wrap(value, width, 2);
        return new(label, value, true, font.Measure(label).Y + 6 + font.Measure(value).Y);
    }

    public readonly record struct MenuText(string Label, string? Value, bool Stacked, float Height);

    private static int RowsOffset(GameScreen screen) =>
        screen switch
        {
            GameScreen.Main => 14 + RowPadding,
            GameScreen.Bindings => 84 + LineThickness + RowPadding,
            _ => 60 + LineThickness + RowPadding,
        };

    private static int FooterHeight(GameScreen screen) => screen == GameScreen.ViewPlayers ? 82 : 50;

    public static int HeaderLine(GameScreen screen, Rectangle panel) =>
        panel.Top + RowsOffset(screen) - RowPadding - LineThickness;

    public static int FooterLine(GameScreen screen, Rectangle panel) => panel.Bottom - FooterHeight(screen);

    public static int SectionLine(Rectangle row) => row.Top - RowPadding - LineThickness;

    public static Rectangle BindingPageButton(Rectangle panel, int direction) =>
        new(direction < 0 ? panel.Left + 12 : panel.Right - 48, panel.Top + 16, 36, 64);

    public static Rectangle PlayerAction(Rectangle panel, bool secondary, bool paired)
    {
        if (secondary && paired)
            return new(panel.Left + 18, panel.Bottom - 44, (panel.Width - 44) / 2, 30);
        return new(panel.Left + 18, panel.Bottom - 76, panel.Width - 36, 30);
    }

    public static Rectangle PlayerBack(Rectangle panel, bool paired) =>
        paired
            ? new(panel.Center.X + 4, panel.Bottom - 44, (panel.Width - 44) / 2, 30)
            : new(panel.Left + 18, panel.Bottom - 44, panel.Width - 36, 30);

    public static Rectangle FooterBack(Rectangle panel) =>
        new(panel.Center.X, panel.Bottom - 48, panel.Width / 2 - 18, 36);

    public static int RoomCell(int room) => LobbyLayout.Cell(room);

    public static Rectangle Room(int room) => Scale(LobbyLayout.Room(room));

    public static Rectangle RoomInterior(int room) => Scale(LobbyLayout.Interior(room));

    public static Rectangle SlotAction(int room, int index)
    {
        var interior = RoomInterior(room);
        return new(interior.Left + 18, interior.Bottom - 86 + index * 26, interior.Width - 36, 24);
    }

    public static Rectangle SlotBack => new(32, 678, 160, 32);

    private static Rectangle Scale(Rectangle source)
    {
        int left = source.Left * Renderer.Width / LobbyLayout.Width;
        int top = source.Top * Renderer.Height / LobbyLayout.Height;
        int right = source.Right * Renderer.Width / LobbyLayout.Width;
        int bottom = source.Bottom * Renderer.Height / LobbyLayout.Height;
        return new(left, top, right - left, bottom - top);
    }

    public static Point? Pointer(Point mouse, int windowWidth, int windowHeight)
    {
        float scale = Math.Min(windowWidth / (float)Renderer.Width, windowHeight / (float)Renderer.Height);
        if (scale <= 0)
            return null;
        float x = (mouse.X - (windowWidth - Renderer.Width * scale) * .5f) / scale;
        float y = (mouse.Y - (windowHeight - Renderer.Height * scale) * .5f) / scale;
        return x >= 0 && x < Renderer.Width && y >= 0 && y < Renderer.Height ? new Point((int)x, (int)y) : null;
    }
}
