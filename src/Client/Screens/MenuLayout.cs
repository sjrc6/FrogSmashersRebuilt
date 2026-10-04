using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class MenuLayout
{
    private const int RowPadding = 7;
    private const int LineThickness = 2;
    private const int SectionSpacing = RowPadding * 2 + LineThickness;

    public sealed record Measurement(Rectangle Panel, MeasuredRow[] Rows, int Page, int[] PageStarts)
    {
        public bool Paginated => PageStarts.Length > 1;
    }

    public readonly record struct MeasuredRow(Rectangle Bounds, MenuText Text);

    public static Measurement Measure(
        GameScreen screen,
        IReadOnlyList<MenuEntry> entries,
        BitmapFont font,
        bool showSticks = false,
        int selected = 0,
        Rectangle? viewport = null,
        Func<MenuEntry, Vector2>? accessorySize = null
    )
    {
        var view = viewport ?? new Rectangle(0, 0, Renderer.Width, Renderer.Height);
        const int margin = 24;
        int preferredWidth = screen switch
        {
            GameScreen.Main => 240,
            GameScreen.Bindings => 480,
            GameScreen.BrowseSteam or GameScreen.BrowseLan => 560,
            _ => 440,
        };
        int width = Math.Min(preferredWidth, view.Width - margin * 2);
        int header = RowsOffset(screen);
        int footer = (showSticks ? 32 : 0) + RowPadding + FooterHeight(screen);
        int capacity = view.Height - margin * 2 - 24 - header - footer;
        if (width < 128 || capacity < 48)
            throw new ArgumentException("Menu viewport is too small");
        var rows = new MeasuredRow[entries.Count];
        var pages = new List<int> { 0 };
        var heights = new List<int>();
        int used = 0;
        int page = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var text = Text(entry, width, font, accessorySize);
            if (entry.IsTitle)
            {
                rows[index] = new(new Rectangle(12, 12, width - 24, 72), text);
                continue;
            }
            int rowHeight = (int)MathF.Ceiling(text.Height) + RowPadding * 2;
            int separator = entry.SeparatorBefore && used > 0 ? SectionSpacing : 0;
            if (rowHeight > capacity)
                throw new ArgumentException("Menu row exceeds the viewport");
            if (used + separator + rowHeight > capacity)
            {
                heights.Add(used);
                pages.Add(index);
                used = 0;
                separator = 0;
            }
            used += separator;
            rows[index] = new(new Rectangle(12, header + used, width - 24, rowHeight), text);
            used += rowHeight;
        }
        heights.Add(used);
        selected = Math.Clamp(selected, 0, Math.Max(0, entries.Count - 1));
        for (int index = 1; index < pages.Count; index++)
            if (pages[index] <= selected)
                page = index;
        int height = header + heights.Max() + footer;
        int top = screen == GameScreen.Main ? view.Top + 324 : view.Top + (view.Height - height) / 2;
        top = Math.Clamp(top, view.Top + margin, view.Bottom - margin - 24 - height);
        var panel = new Rectangle(view.Center.X - width / 2, top, width, height);
        int end = page + 1 < pages.Count ? pages[page + 1] : entries.Count;
        for (int index = 0; index < rows.Length; index++)
        {
            bool visible = entries[index].IsTitle || index >= pages[page] && index < end;
            var bounds = rows[index].Bounds;
            if (visible)
                bounds.Offset(panel.Location);
            else
                bounds = Rectangle.Empty;
            rows[index] = rows[index] with { Bounds = bounds };
        }
        return new(panel, rows, page, pages.ToArray());
    }

    public static Rectangle PageButton(Measurement layout, int direction) =>
        new(
            direction < 0 ? layout.Panel.Left : layout.Panel.Center.X,
            layout.Panel.Bottom + 2,
            layout.Panel.Width / 2,
            20
        );

    private static MenuText Text(
        MenuEntry entry,
        int panelWidth,
        BitmapFont font,
        Func<MenuEntry, Vector2>? accessorySize
    )
    {
        float width = panelWidth - 64;
        string label;
        if (entry.Key != null || entry.Button != null)
        {
            var size =
                accessorySize?.Invoke(entry) ?? throw new ArgumentException("Binding rows require glyph measurements");
            label = font.Wrap(entry.Label, width - size.X - 16, 2);
            return new(label, null, false, Math.Max(size.Y, font.Measure(label).Y));
        }
        if (entry.Value is not { } value)
        {
            label = font.Wrap(entry.Label, width, 2);
            return new(label, null, false, font.Measure(label).Y);
        }
        float valueWidth = Math.Max(font.Measure(value).X, font.Measure(entry.ValueSample ?? value).X);
        if (valueWidth <= width * .55f)
        {
            label = font.Wrap(entry.Label, width - valueWidth - 16, 2);
            return new(label, value, false, Math.Max(font.Measure(label).Y, font.Measure(value).Y));
        }
        label = font.Wrap(entry.Label, width, 1);
        value = font.Wrap(value, width, 2);
        float reservedHeight = Math.Max(
            font.Measure(value).Y,
            font.Measure(font.Wrap(entry.ValueSample ?? value, width, 2)).Y
        );
        return new(label, value, true, font.Measure(label).Y + 6 + reservedHeight);
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
