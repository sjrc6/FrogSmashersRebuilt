using System.Buffers.Binary;

namespace FrogSmashers.Client;

public sealed class LoopResampler
{
    private readonly DecodedSound data;
    private readonly int frames;
    private double phase;
    private float rate = 1;

    public LoopResampler(DecodedSound data)
    {
        this.data = data;
        frames = data.Pcm.Length / (data.Channels * 2);
        if (frames == 0 || data.Channels is not (1 or 2))
        {
            throw new ArgumentException("A loop needs mono or stereo PCM frames", nameof(data));
        }
    }

    public double Position => phase;

    public float Rate
    {
        get => rate;
        set
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            rate = value;
        }
    }

    public void Reset() => phase = 0;

    public void Render(Span<byte> output)
    {
        if (output.Length % (data.Channels * 2) != 0)
        {
            throw new ArgumentException("Incomplete PCM frame", nameof(output));
        }

        for (int offset = 0; offset < output.Length; offset += data.Channels * 2)
        {
            int first = (int)phase;
            int second = (first + 1) % frames;
            float fraction = (float)(phase - first);
            for (int channel = 0; channel < data.Channels; channel++)
            {
                int a = BinaryPrimitives.ReadInt16LittleEndian(
                    data.Pcm.AsSpan((first * data.Channels + channel) * 2, 2)
                );
                int b = BinaryPrimitives.ReadInt16LittleEndian(
                    data.Pcm.AsSpan((second * data.Channels + channel) * 2, 2)
                );
                short sample = (short)Math.Clamp(MathF.Round(a + (b - a) * fraction), short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(output.Slice(offset + channel * 2, 2), sample);
            }

            phase += rate;
            phase -= Math.Floor(phase / frames) * frames;
        }
    }
}
