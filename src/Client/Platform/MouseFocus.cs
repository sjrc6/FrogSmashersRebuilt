using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class MouseFocus
{
    private static readonly IntPtr library = NativeLibrary.Load(
        OperatingSystem.IsWindows() ? "SDL2.dll" : "libSDL2-2.0.so.0",
        typeof(Game).Assembly,
        null
    );
    private static readonly GetMouseFocus getMouseFocus = Marshal.GetDelegateForFunctionPointer<GetMouseFocus>(
        NativeLibrary.GetExport(library, "SDL_GetMouseFocus")
    );

    public static bool IsOver(GameWindow window) => getMouseFocus() == window.Handle;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetMouseFocus();
}
