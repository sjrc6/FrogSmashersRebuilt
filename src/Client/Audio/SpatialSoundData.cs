using System.Buffers.Binary;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class SpatialSoundData
{
    private readonly float[] samples;
    public int SampleRate { get; }
    public int Frames => samples.Length;

    public SpatialSoundData(DecodedSound data)
    {
        SampleRate = data.SampleRate;
        samples = new float[data.Pcm.Length / (2 * data.Channels)];
        for (int frame = 0; frame < samples.Length; frame++)
        for (int channel = 0; channel < data.Channels; channel++)
            samples[frame] +=
                BinaryPrimitives.ReadInt16LittleEndian(data.Pcm.AsSpan((frame * data.Channels + channel) * 2, 2))
                / 32768f;
    }

    public int Render(int firstFrame, Vector2 gains, Span<byte> output)
    {
        int frames = Math.Min(output.Length / 4, samples.Length - firstFrame);
        for (int frame = 0; frame < frames; frame++)
        {
            float sample = samples[firstFrame + frame];
            BinaryPrimitives.WriteInt16LittleEndian(output.Slice(frame * 4, 2), DecodedSound.ToPcm16(sample * gains.X));
            BinaryPrimitives.WriteInt16LittleEndian(
                output.Slice(frame * 4 + 2, 2),
                DecodedSound.ToPcm16(sample * gains.Y)
            );
        }
        return frames;
    }
}
