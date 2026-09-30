using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class MenuLayout
{
    public static Rectangle Row(GameScreen screen, int index, int count = 0, int room = 0) =>
        screen switch
        {
            GameScreen.Main => new(480, 335 + index * 49, 320, 43),
            GameScreen.Playing => new(410, 286 + index * 49, 460, 43),
            GameScreen.Connecting => new(410, 390 + index * 49, 460, 43),
            GameScreen.Error => new(390, 550, 500, 43),
            GameScreen.SlotEditor or GameScreen.SlotOptions => SlotRow(room, index),
            GameScreen.LobbyMenu => new(415, 360 - (count * 43 - 4) / 2 + 19 + index * 43, 450, 39),
            _ => new(380, 154 + index * 49, 520, 43),
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
