using System.Buffers.Binary;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

public sealed class AudioSpatializer
{
    public Vector2 Gains(Vector3 source) => Gains(source, new Vector3(0, 0, -10));

    public Vector2 Gains(Vector3 source, Vector3 listener)
    {
        var offset = source - listener;
        float distance = offset.Length();
        float pan = distance > 0 ? Math.Clamp(offset.X / distance, -1, 1) : 0;
        float combinedGain = MathF.Sqrt(2 - pan * pan);
        float attenuation = Math.Clamp((500 - distance) / 499, 0, 1);
        return new Vector2(combinedGain - pan, combinedGain + pan) * (0.5f * attenuation);
    }

    public static byte[] SpatialPcm(DecodedSound data, Vector2 gains)
    {
        int frames = data.Pcm.Length / (2 * data.Channels);
        var stereo = new byte[frames * 4];
        for (int frame = 0; frame < frames; frame++)
        {
            float mono = 0;
            for (int channel = 0; channel < data.Channels; channel++)
            {
                mono +=
                    BinaryPrimitives.ReadInt16LittleEndian(data.Pcm.AsSpan((frame * data.Channels + channel) * 2, 2))
                    / 32768f;
            }

            BinaryPrimitives.WriteInt16LittleEndian(stereo.AsSpan(frame * 4, 2), DecodedSound.ToPcm16(mono * gains.X));
            BinaryPrimitives.WriteInt16LittleEndian(
                stereo.AsSpan(frame * 4 + 2, 2),
                DecodedSound.ToPcm16(mono * gains.Y)
            );
        }

        return stereo;
    }
}
