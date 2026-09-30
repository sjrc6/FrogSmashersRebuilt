using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class MenuLayout
{
    public static Rectangle Panel(GameScreen screen, int count)
    {
        int width = screen switch
        {
            GameScreen.Main => 368,
            GameScreen.LobbyMenu => 474,
            GameScreen.Playing or GameScreen.Connecting => 484,
            GameScreen.Error => 1040,
            _ => 544,
        };
        int height = RowsOffset(screen) + count * RowSpacing(screen) + 80;
        int top = screen == GameScreen.Main ? 324 : (Renderer.Height - height) / 2;
        return new((Renderer.Width - width) / 2, top, width, height);
    }

    public static Rectangle Row(GameScreen screen, int index, int count = 0, int room = 0)
    {
        if (screen is GameScreen.SlotEditor or GameScreen.SlotOptions)
            return SlotRow(room, index);
        var panel = Panel(screen, count);
        int spacing = RowSpacing(screen);
        return new(panel.X + 12, panel.Y + RowsOffset(screen) + index * spacing, panel.Width - 24, spacing - 4);
    }

    private static int RowSpacing(GameScreen screen) => screen == GameScreen.LobbyMenu ? 44 : 48;

    private static int RowsOffset(GameScreen screen) =>
        screen switch
        {
            GameScreen.Main => 20,
            GameScreen.Connecting => 152,
            GameScreen.Error => 296,
            _ => 56,
        };

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
