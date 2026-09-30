using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class LobbyLayout
{
    public const int Width = 960;
    public const int Height = 540;
    public const int WallThickness = 12;
    public const int OutlineThickness = WallThickness / 2;

    public static int Cell(int room) => room < 4 ? room : room + 1;

    public static Rectangle Room(int room)
    {
        int cell = Cell(room);
        return new(45 + cell % 3 * 290, 45 + cell / 3 * 150, 290, 150);
    }

    public static Rectangle Interior(int room)
    {
        var bounds = Room(room);
        int column = Cell(room) % 3;
        int row = Cell(room) / 3;
        int left = bounds.Left + (column == 0 ? 0 : WallThickness / 2);
        int right = bounds.Right - (column == 2 ? 0 : WallThickness / 2);
        int top = bounds.Top + (row == 0 ? 0 : WallThickness);
        return new(left, top, right - left, bounds.Bottom - top);
    }

    public static Rectangle[] SelectionEdges(int room)
    {
        var bounds = Interior(room);
        int left = bounds.Left,
            right = bounds.Right,
            top = bounds.Top,
            bottom = bounds.Bottom;
        int thickness = OutlineThickness;
        return
        [
            new(left - thickness, top - thickness, right - left + thickness * 2, thickness),
            new(left - thickness, bottom, right - left + thickness * 2, thickness),
            new(left - thickness, top - thickness, thickness, bottom - top + thickness * 2),
            new(right, top - thickness, thickness, bottom - top + thickness * 2),
        ];
    }
}
