using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

internal static class AudioBackendTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Open(IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Create(IntPtr device, int[] attributes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetCurrent();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte Current(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Destroy(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte Close(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Integers(IntPtr device, int parameter, int count, [Out] int[] values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Generate(int count, out uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Delete(int count, ref uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Buffer(uint id, int format, byte[] data, int bytes, int rate);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SourceInt(uint id, int parameter, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SourceFloat(uint id, int parameter, float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SourceFloats(uint id, int parameter, float[] values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SourceCommand(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Render(IntPtr device, [Out] float[] samples, int frames);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Error();

    public static void Run(string content, Action<bool, string> check)
    {
        using var native = new Native();
        var previous = native.Bind<GetCurrent>("alcGetCurrentContext")();
        var device = native.Bind<Open>("alcLoopbackOpenDeviceSOFT")(IntPtr.Zero);
        check(device != IntPtr.Zero, "bundled OpenAL loopback unavailable");

        var context = native.Bind<Create>("alcCreateContext")(
            device,
            [0x1007, 48000, 0x1990, 0x1501, 0x1991, 0x1406, 0x19ac, 0x19b2, 0x199a, 0, 0]
        );
        try
        {
            check(
                context != IntPtr.Zero && native.Bind<Current>("alcMakeContextCurrent")(context) != 0,
                "create HRTF loopback context"
            );
            int Mode()
            {
                int[] result = [0];
                native.Bind<Integers>("alcGetIntegerv")(device, 0x19ac, 1, result);
                return result[0];
            }

            check(Mode() == 0x19b2, "the failure-case device must actually enable HRTF");
            var spatializer = new AudioSpatializer();
            var sample = DecodedSound.Read(Path.Combine(content, "Audio/Sound/Frog_Knockout_Lvl1.ogg"));
            var pcm = AudioSpatializer.SpatialPcm(sample, spatializer.Gains(Vector3.Zero) * (.55f * .4f * .65f));
            float[] before = Play(native, device, pcm);
            double mismatch = RmsError(pcm, before);
            check(mismatch > .005, "HRTF reproduction must demonstrate waveform coloration before the fix");
            Console.WriteLine(AudioOutput.ConfigurePlainStereo());
            check(Mode() == 0x19ae, "audio initialization must replace HRTF with basic stereo");

            foreach (string level in new[] { "Lvl1", "Lvl2", "Lvl3", "Suicide" })
            {
                foreach (float x in new[] { -40f, 0f, 40f })
                {
                    sample = DecodedSound.Read(Path.Combine(content, $"Audio/Sound/Frog_Knockout_{level}.ogg"));
                    pcm = AudioSpatializer.SpatialPcm(sample, spatializer.Gains(new Vector3(x, 0, 0)) * .2f);
                    var after = Play(native, device, pcm);
                    check(
                        RmsError(pcm, after) < 1e-7,
                        $"native {level} output changes PCM rate, pitch or gain at x={x}"
                    );

                    float end =
                        Math.Max(
                            Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(pcm.Length - 4, 2))),
                            Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(pcm.Length - 2, 2)))
                        ) / 32768f;
                    check(
                        after.Skip(pcm.Length / 2).All(s => Math.Abs(s) <= end + 1e-7)
                            && after.Skip(pcm.Length / 2 + 1024).All(s => Math.Abs(s) < 1e-7),
                        $"native {level} output adds a tail beyond its final-sample fade"
                    );
                }
            }

            Console.WriteLine(
                $"Native KO waveform checks passed; HRTF failure-case RMS error {mismatch:F6}, corrected <0.0000001"
            );
        }
        finally
        {
            native.Bind<Current>("alcMakeContextCurrent")(previous);
            if (context != IntPtr.Zero)
            {
                native.Bind<Destroy>("alcDestroyContext")(context);
            }

            native.Bind<Close>("alcCloseDevice")(device);
        }
    }

    private static float[] Play(Native native, IntPtr device, byte[] pcm)
    {
        native.Bind<Generate>("alGenBuffers")(1, out uint buffer);
        native.Bind<Generate>("alGenSources")(1, out uint source);
        try
        {
            native.Bind<Buffer>("alBufferData")(buffer, 0x1103, pcm, pcm.Length, 48000);
            native.Bind<SourceInt>("alSourcei")(source, 0x1009, (int)buffer);
            native.Bind<SourceInt>("alSourcei")(source, 0x202, 1);
            native.Bind<SourceFloat>("alSourcef")(source, 0x1003, 1);
            native.Bind<SourceFloat>("alSourcef")(source, 0x100a, 1);

            native.Bind<SourceFloats>("alSourcefv")(source, 0x1030, [MathF.PI / 6, -MathF.PI / 6]);
            native.Bind<SourceCommand>("alSourcePlay")(source);
            var output = new float[pcm.Length / 2 + 2048];
            native.Bind<Render>("alcRenderSamplesSOFT")(device, output, output.Length / 2);
            if (native.Bind<Error>("alGetError")() != 0)
            {
                throw new Exception("OpenAL KO loopback error");
            }

            return output;
        }
        finally
        {
            native.Bind<SourceCommand>("alSourceStop")(source);
            native.Bind<Delete>("alDeleteSources")(1, ref source);
            native.Bind<Delete>("alDeleteBuffers")(1, ref buffer);
        }
    }

    private static double RmsError(byte[] pcm, float[] output)
    {
        double sum = 0;
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            double difference = output[i] - BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2, 2)) / 32768.0;
            sum += difference * difference;
        }

        return Math.Sqrt(sum / (pcm.Length / 2));
    }

    private sealed class Native : IDisposable
    {
        private readonly IntPtr library = NativeLibrary.Load(
            OperatingSystem.IsWindows() ? "openal.dll" : "libopenal.so",
            typeof(SoundEffect).Assembly,
            null
        );

        public T Bind<T>(string symbol)
            where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));

        public void Dispose() => NativeLibrary.Free(library);
    }
}
