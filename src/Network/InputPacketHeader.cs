namespace FrogSmashers.Network;

internal readonly record struct InputPacketHeader(
    long Tick,
    long LastReceivedFrame,
    long ConfirmedFrame,
    long HashFrame,
    ulong Hash,
    long FirstInputFrame,
    byte InputCount
)
{
    public static InputPacketHeader Read(BinaryReader reader)
    {
        return new InputPacketHeader(
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadInt64(),
            reader.ReadUInt64(),
            reader.ReadInt64(),
            reader.ReadByte()
        );
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Tick);
        writer.Write(LastReceivedFrame);
        writer.Write(ConfirmedFrame);
        writer.Write(HashFrame);
        writer.Write(Hash);
        writer.Write(FirstInputFrame);
        writer.Write(InputCount);
    }
}
