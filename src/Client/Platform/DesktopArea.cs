using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal static class DesktopArea
{
    public static Rectangle Primary()
    {
        if (OperatingSystem.IsWindows() && SystemParametersInfoW(0x30, 0, out var area, 0))
            return new(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top);
        var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        return new(0, 0, mode.Width, mode.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WorkArea
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out WorkArea area, uint flags);
}
