using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Audio;

namespace FrogSmashers.Client;

public static class AudioOutput
{
    private const int OutputMode = 0x19ac;
    private const int StereoBasic = 0x19ae;
    private const int Frequency = 0x1007;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetContext();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetDevice(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte HasExtension(IntPtr device, [MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetIntegers(IntPtr device, int parameter, int count, [Out] int[] values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ResetDevice(IntPtr device, int[] attributes);

    public static string ConfigurePlainStereo()
    {
        IntPtr library = IntPtr.Zero;
        try
        {
            string name = OperatingSystem.IsWindows() ? "openal.dll" : "libopenal.so";
            library = NativeLibrary.Load(name, typeof(SoundEffect).Assembly, null);
            T Bind<T>(string symbol)
                where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));
            var context = Bind<GetContext>("alcGetCurrentContext")();
            if (context == IntPtr.Zero)
            {
                return "Audio output: no current OpenAL context; retained device defaults.";
            }

            var device = Bind<GetDevice>("alcGetContextsDevice")(context);
            if (Bind<HasExtension>("alcIsExtensionPresent")(device, "ALC_SOFT_output_mode") == 0)
            {
                return "Audio output: basic stereo request unavailable; retained device defaults.";
            }

            var get = Bind<GetIntegers>("alcGetIntegerv");
            int Read(int parameter)
            {
                int[] value = [0];
                get(device, parameter, 1, value);
                return value[0];
            }

            int previous = Read(OutputMode);
            int rate = Read(Frequency);
            if (previous == StereoBasic)
            {
                return $"Audio output: basic stereo, {rate} Hz.";
            }

            int[] existing = new int[Read(0x1002)];
            get(device, 0x1003, existing.Length, existing);
            var attributes = new List<int> { OutputMode, StereoBasic, Frequency, rate };
            for (int i = 0; i + 1 < existing.Length && existing[i] != 0; i += 2)
            {
                if (existing[i] is 0x1990 or 0x1991 or 0x199a)
                {
                    attributes.AddRange([existing[i], existing[i + 1]]);
                }
            }

            attributes.Add(0);
            bool reset = Bind<ResetDevice>("alcResetDeviceSOFT")(device, attributes.ToArray()) != 0;
            if (!reset || Read(OutputMode) != StereoBasic)
            {
                return $"Audio output: device rejected basic stereo request (previous mode 0x{previous:x}); retained device output.";
            }

            return $"Audio output: basic stereo, {Read(Frequency)} Hz (previous mode 0x{previous:x}).";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return "Audio output: basic stereo request unavailable; " + ex.Message;
        }
        finally
        {
            if (library != IntPtr.Zero)
            {
                NativeLibrary.Free(library);
            }
        }
    }
}
