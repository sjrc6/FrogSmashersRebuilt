namespace GGCS.Protocol;

internal static class InputCompression
{
    internal const int MaximumDecodedBytes = 65507;

    internal static byte[] EncodePacket(
        IReadOnlyList<byte[]> frames,
        int start,
        int inputSize,
        int byteBudget,
        out int count
    )
    {
        count = Math.Min(frames.Count - start, MaximumDecodedBytes / inputSize);
        byte[] encoded = Encode(frames, start, count, inputSize);
        if (encoded.Length <= byteBudget)
            return encoded;

        int low = 1;
        int high = count - 1;
        byte[] best = [];
        count = 0;
        while (low <= high)
        {
            int candidate = low + (high - low) / 2;
            encoded = Encode(frames, start, candidate, inputSize);
            if (encoded.Length <= byteBudget)
            {
                count = candidate;
                best = encoded;
                low = candidate + 1;
            }
            else
                high = candidate - 1;
        }
        if (count == 0)
            throw new InvalidOperationException("One input frame exceeds the packet budget.");
        return best;
    }

    internal static byte[] Encode(IReadOnlyList<byte[]> frames, int start, int count, int inputSize)
    {
        byte[] delta = new byte[checked(count * inputSize)];
        for (int frame = 0; frame < count; frame++)
        {
            ReadOnlySpan<byte> current = frames[start + frame];
            for (int index = 0; index < inputSize; index++)
            {
                byte previous = frame == 0 ? (byte)0 : frames[start + frame - 1][index];
                delta[frame * inputSize + index] = (byte)(current[index] ^ previous);
            }
        }

        using var output = new MemoryStream();
        int position = 0;
        while (position < delta.Length)
        {
            bool zeros = delta[position] == 0;
            int countInRun = 1;
            while (
                countInRun < 128 && position + countInRun < delta.Length && (delta[position + countInRun] == 0) == zeros
            )
                countInRun++;

            output.WriteByte((byte)((zeros ? 128 : 0) | (countInRun - 1)));
            if (!zeros)
                output.Write(delta, position, countInRun);
            position += countInRun;
        }
        return output.ToArray();
    }

    internal static bool TryDecode(ReadOnlySpan<byte> encoded, int count, int inputSize, out byte[][] frames)
    {
        frames = [];
        if (count < 1 || inputSize < 1 || (long)count * inputSize > MaximumDecodedBytes)
            return false;

        byte[] delta = new byte[count * inputSize];
        int position = 0;
        while (!encoded.IsEmpty)
        {
            byte header = encoded[0];
            encoded = encoded[1..];
            int run = (header & 127) + 1;
            if (run > delta.Length - position)
                return false;
            if ((header & 128) == 0)
            {
                if (encoded.Length < run)
                    return false;
                encoded[..run].CopyTo(delta.AsSpan(position));
                encoded = encoded[run..];
            }
            position += run;
        }
        if (position != delta.Length)
            return false;

        frames = new byte[count][];
        for (int frame = 0; frame < count; frame++)
        {
            frames[frame] = delta.AsSpan(frame * inputSize, inputSize).ToArray();
            if (frame == 0)
                continue;
            for (int index = 0; index < inputSize; index++)
                frames[frame][index] ^= frames[frame - 1][index];
        }
        return true;
    }
}
