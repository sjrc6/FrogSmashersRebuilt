using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class MenuLayout
{
    public static Rectangle Panel(GameScreen screen, int count)
    {
        int width = screen switch
        {
            GameScreen.Main => 240,
            GameScreen.Bindings => 440,
            _ => 400,
        };
        int extra = ExpandedRow(screen) >= 0 ? 24 : 0;
        int footer = screen == GameScreen.Main ? 64 : 96;
        int height = RowsOffset(screen) + count * RowSpacing(screen) + extra + footer;
        int top = screen == GameScreen.Main ? 324 : (Renderer.Height - height) / 2;
        return new((Renderer.Width - width) / 2, top, width, height);
    }

    public static Rectangle Row(GameScreen screen, int index, int count = 0, int room = 0)
    {
        if (screen is GameScreen.SlotEditor or GameScreen.SlotOptions)
            return SlotRow(room, index);
        var panel = Panel(screen, count);
        int spacing = RowSpacing(screen);
        int expanded = ExpandedRow(screen);
        int offset = expanded >= 0 && index > expanded ? 24 : 0;
        int height = spacing - 4 + (index == expanded ? 24 : 0);
        return new(panel.X + 12, panel.Y + RowsOffset(screen) + index * spacing + offset, panel.Width - 24, height);
    }

    private static int ExpandedRow(GameScreen screen) =>
        screen switch
        {
            GameScreen.MatchSettings => 3,
            GameScreen.JoinSteam or GameScreen.JoinUdp => 0,
            _ => -1,
        };

    private static int RowSpacing(GameScreen screen) => screen == GameScreen.LobbyMenu ? 44 : 48;

    private static int RowsOffset(GameScreen screen) =>
        screen switch
        {
            GameScreen.Main => 20,
            GameScreen.Bindings => 96,
            _ => 72,
        };

    public static Rectangle BindingPageButton(Rectangle panel, int direction) =>
        new(direction < 0 ? panel.Left + 12 : panel.Right - 48, panel.Top + 12, 36, 64);

    private static Rectangle SlotRow(int room, int index)
    {
        var interior = RoomInterior(room);
        return new(interior.X + 12, interior.Y + 49 + index * 34, interior.Width - 24, 30);
    }

    public static int RoomCell(int room) => LobbyLayout.Cell(room);

    public static Rectangle Room(int room) => Scale(LobbyLayout.Room(room));

    public static Rectangle RoomInterior(int room) => Scale(LobbyLayout.Interior(room));

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
