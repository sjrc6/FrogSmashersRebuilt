using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GGCS;

public static class SessionLink
{
    public static ulong Identity(ulong session, int firstPeer, int secondPeer, int firstFrame)
    {
        Span<byte> input = stackalloc byte[20];
        BinaryPrimitives.WriteUInt64LittleEndian(input, session);
        BinaryPrimitives.WriteInt32LittleEndian(input[8..], Math.Min(firstPeer, secondPeer));
        BinaryPrimitives.WriteInt32LittleEndian(input[12..], Math.Max(firstPeer, secondPeer));
        BinaryPrimitives.WriteInt32LittleEndian(input[16..], firstFrame);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash) | 1;
    }
}
