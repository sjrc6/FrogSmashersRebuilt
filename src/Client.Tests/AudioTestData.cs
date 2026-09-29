using System.Buffers.Binary;

internal static class AudioTestData
{
    public static byte[] Pcm(params short[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), values[i]);
        }

        return bytes;
    }

    public static short Sample(byte[] bytes, int index) =>
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(index * 2, 2));
}
