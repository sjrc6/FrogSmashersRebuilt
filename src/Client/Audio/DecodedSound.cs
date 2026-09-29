namespace FrogSmashers.Client;

public sealed record DecodedSound(byte[] Pcm, int SampleRate, int Channels)
{
    public static DecodedSound Read(string path)
    {
        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return ReadWave(path);
        }

        using var reader = new NVorbis.VorbisReader(path);
        var samples = new float[checked((int)reader.TotalSamples * reader.Channels)];
        int total = 0;
        int read;
        while (total < samples.Length && (read = reader.ReadSamples(samples, total, samples.Length - total)) > 0)
        {
            total += read;
        }

        if (total != samples.Length || reader.Channels is not (1 or 2))
        {
            throw new InvalidDataException("Incomplete or unsupported audio: " + path);
        }

        var pcm = new byte[total * 2];
        for (int i = 0; i < total; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), ToPcm16(samples[i]));
        }

        return new(pcm, reader.SampleRate, reader.Channels);
    }

    public static short ToPcm16(float sample) =>
        (short)Math.Clamp(MathF.Round(sample * 32768), short.MinValue, short.MaxValue);

    public static DecodedSound ReadWave(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != 0x46464952)
        {
            throw new InvalidDataException("Expected RIFF audio: " + path);
        }

        reader.ReadUInt32();
        if (reader.ReadUInt32() != 0x45564157)
        {
            throw new InvalidDataException("Expected WAVE audio: " + path);
        }

        ushort format = 0;
        ushort channels = 0;
        ushort bits = 0;
        int rate = 0;
        byte[]? samples = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            uint tag = reader.ReadUInt32();
            int size = checked((int)reader.ReadUInt32());
            long end = reader.BaseStream.Position + size;
            if (end > reader.BaseStream.Length)
            {
                throw new InvalidDataException("Truncated WAVE audio: " + path);
            }

            if (tag == 0x20746d66)
            {
                if (size < 16)
                {
                    throw new InvalidDataException("Invalid WAVE format: " + path);
                }

                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                rate = reader.ReadInt32();
                reader.ReadUInt32();
                reader.ReadUInt16();
                bits = reader.ReadUInt16();
            }
            else if (tag == 0x61746164)
            {
                samples = reader.ReadBytes(size);
            }

            reader.BaseStream.Position = end + (size & 1);
        }

        if (
            samples == null
            || format != 1
            || bits != 16
            || channels is not (1 or 2)
            || rate < 8000
            || rate > 48000
            || samples.Length % (channels * 2) != 0
        )
        {
            throw new InvalidDataException("Expected 16-bit mono/stereo PCM WAVE: " + path);
        }

        return new(samples, rate, channels);
    }
}
