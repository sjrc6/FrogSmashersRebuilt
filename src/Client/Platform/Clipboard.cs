using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class Clipboard
{
    public static string ReadText()
    {
        string name = OperatingSystem.IsWindows() ? "SDL2.dll" : "libSDL2-2.0.so.0";
        var library = NativeLibrary.Load(name, typeof(Game).Assembly, null);
        try
        {
            var read = Marshal.GetDelegateForFunctionPointer<GetText>(
                NativeLibrary.GetExport(library, "SDL_GetClipboardText")
            );
            var free = Marshal.GetDelegateForFunctionPointer<Free>(NativeLibrary.GetExport(library, "SDL_free"));
            var text = read();
            try
            {
                return Marshal.PtrToStringUTF8(text) ?? "";
            }
            finally
            {
                free(text);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetText();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Free(IntPtr text);
}
