using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FrogSmashers.Client;

internal sealed class WindowsWindowIcons : IDisposable
{
    private const uint SetIcon = 0x80;
    private readonly IntPtr window;
    private readonly IntPtr previousSmall;
    private readonly IntPtr previousLarge;
    private IntPtr small;
    private IntPtr large;

    public WindowsWindowIcons(IntPtr sdlWindow)
    {
        var sdl = GetModuleHandleW("SDL2.dll");
        var getInfo = Marshal.GetDelegateForFunctionPointer<GetWindowInfo>(
            NativeLibrary.GetExport(sdl, "SDL_GetWindowWMInfo")
        );
        var info = new WindowInfo { Major = 2 };
        if (getInfo(sdlWindow, ref info) == 0 || info.Subsystem != 1)
            throw new InvalidOperationException("SDL did not provide the Windows window handle.");
        window = info.Window;

        using var stream = typeof(WindowsWindowIcons).Assembly.GetManifestResourceStream("Window.ico")!;
        using var reader = new BinaryReader(stream);
        uint dpi = GetDpiForWindow(window);
        small = LoadIcon(reader, GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi));
        try
        {
            large = LoadIcon(reader, GetSystemMetricsForDpi(11, dpi), GetSystemMetricsForDpi(12, dpi));
        }
        catch
        {
            DestroyIcon(small);
            throw;
        }
        previousSmall = SendMessageW(window, SetIcon, IntPtr.Zero, small);
        previousLarge = SendMessageW(window, SetIcon, new IntPtr(1), large);
    }

    private static IntPtr LoadIcon(BinaryReader reader, int width, int height)
    {
        reader.BaseStream.Position = 4;
        int count = reader.ReadUInt16();
        int bestDistance = int.MaxValue;
        int length = 0;
        int offset = 0;
        for (int index = 0; index < count; index++)
        {
            int size = reader.ReadByte();
            if (size == 0)
                size = 256;
            reader.BaseStream.Seek(7, SeekOrigin.Current);
            int frameLength = reader.ReadInt32();
            int frameOffset = reader.ReadInt32();
            int distance = Math.Abs(size - width) * 2 + (size < width ? 1 : 0);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            length = frameLength;
            offset = frameOffset;
        }
        reader.BaseStream.Position = offset;
        byte[] pixels = reader.ReadBytes(length);
        var icon = CreateIconFromResourceEx(pixels, (uint)pixels.Length, true, 0x30000, width, height, 0);
        if (icon == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load the window icon.");
        return icon;
    }

    public void Dispose()
    {
        if (small == IntPtr.Zero)
            return;
        SendMessageW(window, SetIcon, IntPtr.Zero, previousSmall);
        SendMessageW(window, SetIcon, new IntPtr(1), previousLarge);
        DestroyIcon(small);
        DestroyIcon(large);
        small = large = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct WindowInfo
    {
        [FieldOffset(0)]
        public byte Major;

        [FieldOffset(4)]
        public int Subsystem;

        [FieldOffset(8)]
        public IntPtr Window;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetWindowInfo(IntPtr sdlWindow, ref WindowInfo info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string name);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        byte[] data,
        uint size,
        [MarshalAs(UnmanagedType.Bool)] bool icon,
        uint version,
        int width,
        int height,
        uint flags
    );

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr parameter, IntPtr value);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
