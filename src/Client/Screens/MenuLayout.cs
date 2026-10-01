using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class MenuLayout
{
    private const int RowPadding = 7;
    private const int LineThickness = 2;
    private const int SectionSpacing = RowPadding * 2 + LineThickness;

    public static Rectangle Panel(
        GameScreen screen,
        IReadOnlyList<MenuEntry> entries,
        BitmapFont font,
        bool showSticks = false
    )
    {
        int width = PanelWidth(screen);
        int rows = entries.Where(entry => !entry.IsTitle).Sum(entry => RowHeight(entry, width, font));
        int sections = entries.Count(entry => entry.SeparatorBefore) * SectionSpacing;
        int height = RowsOffset(screen) + rows + sections + (showSticks ? 32 : 0) + RowPadding + FooterHeight(screen);
        int top = screen == GameScreen.Main ? 324 : (Renderer.Height - height) / 4 * 2;
        return new((Renderer.Width - width) / 2, top, width, height);
    }

    public static Rectangle Row(
        GameScreen screen,
        int index,
        IReadOnlyList<MenuEntry> entries,
        BitmapFont font,
        int room = 0,
        bool showSticks = false
    )
    {
        if (screen == GameScreen.SlotEditor)
            return SlotRow(room, index);
        var panel = Panel(screen, entries, font, showSticks);
        int sections = entries.Take(index + 1).Count(entry => entry.SeparatorBefore) * SectionSpacing;
        if (entries[index].IsTitle)
            return new(panel.Left + 12, panel.Top + 12, panel.Width - 24, 72);
        int offset = entries
            .Take(index)
            .Where(entry => !entry.IsTitle)
            .Sum(entry => RowHeight(entry, panel.Width, font));
        return new(
            panel.X + 12,
            panel.Y + RowsOffset(screen) + offset + sections,
            panel.Width - 24,
            RowHeight(entries[index], panel.Width, font)
        );
    }

    private static int PanelWidth(GameScreen screen) =>
        screen switch
        {
            GameScreen.Main => 240,
            GameScreen.Bindings => 440,
            _ => 400,
        };

    private static int RowHeight(MenuEntry entry, int panelWidth, BitmapFont font) =>
        (int)MathF.Ceiling(Text(entry, panelWidth, font).Height) + RowPadding * 2;

    public static MenuText Text(MenuEntry entry, int panelWidth, BitmapFont font)
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

    private static Rectangle SlotRow(int room, int index)
    {
        var interior = RoomInterior(room);
        return new(interior.X + 12, interior.Y + 49 + index * 34, interior.Width - 24, 30);
    }

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
