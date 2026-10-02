using System.Buffers.Binary;

namespace GGCS.Protocol;

internal enum PacketKind : byte
{
    Synchronize,
    SynchronizeReply,
    Inputs,
    Ping,
    Pong,
    Checksum,
    Disconnect,
    ChecksumAck,
}

internal enum PacketDecodeFailure
{
    Malformed,
    StaleSession,
    ConfigurationMismatch,
}

internal sealed class ProtocolPacket
{
    public PacketKind Kind;
    public uint Challenge;
    public int SendInputSize;
    public int ReceiveInputSize;
    public int PlayerCount;
    public int InitialFrame;
    public int AcknowledgedFrame;
    public ConnectionStatus[] Statuses = [];
    public int StartFrame;
    public int InputCount;
    public byte[] InputData = [];
    public long Timestamp;
    public int Frame;
    public int Advantage;
    public int TimingRevision;
    public int ResponseDelay;
    public int Donation;
    public int ExtraDelay;
    public int MaxExtraDelay;
    public int CommittedFrame = -1;
    public uint DisconnectMask;
    public int DisconnectFloor = -1;
    public int DisconnectReadyCut = int.MinValue;
    public ulong Checksum;
    public bool ExcludeRemote;
}

internal static class PacketCodec
{
    internal const int HeaderSize = 13;
    internal const int InputHeaderSize = 25;
    internal const int DisconnectProgressSize = 12;
    private const uint Magic = 0x53434747;

    internal static byte[] Encode(ulong sessionId, ulong configurationId, ProtocolPacket packet)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write(sessionId);
        writer.Write((byte)packet.Kind);
        switch (packet.Kind)
        {
            case PacketKind.Synchronize:
            case PacketKind.SynchronizeReply:
                writer.Write(configurationId);
                writer.Write(packet.Challenge);
                writer.Write(packet.SendInputSize);
                writer.Write(packet.ReceiveInputSize);
                writer.Write(packet.PlayerCount);
                writer.Write(packet.InitialFrame);
                break;
            case PacketKind.Inputs:
                writer.Write(packet.DisconnectMask != 0);
                writer.Write(packet.AcknowledgedFrame);
                writer.Write(packet.Frame);
                writer.Write(packet.Advantage);
                writer.Write(packet.CommittedFrame);
                if (packet.DisconnectMask != 0)
                {
                    writer.Write(packet.DisconnectMask);
                    writer.Write(packet.DisconnectFloor);
                    writer.Write(packet.DisconnectReadyCut);
                }
                foreach (ConnectionStatus status in packet.Statuses)
                {
                    uint packed = checked((uint)(status.LastFrame + 1));
                    if (status.Disconnected)
                        packed |= 0x80000000;
                    writer.Write(packed);
                }
                writer.Write(packet.StartFrame);
                writer.Write((ushort)packet.InputCount);
                writer.Write((ushort)packet.InputData.Length);
                writer.Write(packet.InputData);
                break;
            case PacketKind.Ping:
                writer.Write(packet.Timestamp);
                writer.Write(packet.Frame);
                writer.Write(packet.Advantage);
                WriteTiming(writer, packet);
                break;
            case PacketKind.Pong:
                writer.Write(packet.Timestamp);
                break;
            case PacketKind.Checksum:
                writer.Write(packet.Frame);
                writer.Write(packet.Checksum);
                break;
            case PacketKind.ChecksumAck:
                writer.Write(packet.Frame);
                break;
            case PacketKind.Disconnect:
                writer.Write(packet.ExcludeRemote);
                break;
        }
        return stream.ToArray();
    }

    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        ulong sessionId,
        ulong configurationId,
        int playerCount,
        out ProtocolPacket packet,
        out PacketDecodeFailure failure
    )
    {
        packet = new ProtocolPacket();
        failure = PacketDecodeFailure.Malformed;
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            return false;
        if (BinaryPrimitives.ReadUInt64LittleEndian(data[4..]) != sessionId)
        {
            failure = PacketDecodeFailure.StaleSession;
            return false;
        }
        packet.Kind = (PacketKind)data[12];
        data = data[HeaderSize..];
        switch (packet.Kind)
        {
            case PacketKind.Synchronize:
            case PacketKind.SynchronizeReply:
                if (data.Length != 28)
                    return false;
                if (BinaryPrimitives.ReadUInt64LittleEndian(data) != configurationId)
                {
                    failure = PacketDecodeFailure.ConfigurationMismatch;
                    return false;
                }
                data = data[8..];
                packet.Challenge = BinaryPrimitives.ReadUInt32LittleEndian(data);
                packet.SendInputSize = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
                packet.ReceiveInputSize = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
                packet.PlayerCount = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
                packet.InitialFrame = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
                return true;
            case PacketKind.Inputs:
                if (playerCount is < 1 or > 64 || data.Length < InputHeaderSize || data[0] > 1)
                    return false;
                bool disconnecting = data[0] != 0;
                int fixedSize = InputHeaderSize + playerCount * 4 + (disconnecting ? DisconnectProgressSize : 0);
                if (data.Length < fixedSize)
                    return false;
                data = data[1..];
                packet.AcknowledgedFrame = BinaryPrimitives.ReadInt32LittleEndian(data);
                packet.Frame = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
                packet.Advantage = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
                packet.CommittedFrame = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
                data = data[16..];
                if (disconnecting)
                {
                    packet.DisconnectMask = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    packet.DisconnectFloor = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
                    packet.DisconnectReadyCut = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
                    if (packet.DisconnectMask == 0)
                        return false;
                    data = data[DisconnectProgressSize..];
                }
                packet.Statuses = new ConnectionStatus[playerCount];
                for (int index = 0; index < playerCount; index++)
                {
                    uint packed = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    packet.Statuses[index] = new ConnectionStatus(
                        (packed & 0x80000000) != 0,
                        (int)(packed & 0x7fffffff) - 1
                    );
                    data = data[4..];
                }
                packet.StartFrame = BinaryPrimitives.ReadInt32LittleEndian(data);
                packet.InputCount = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
                int length = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
                data = data[8..];
                if (length != data.Length)
                    return false;
                packet.InputData = data.ToArray();
                return true;
            case PacketKind.Ping:
                if (data.Length != 36)
                    return false;
                packet.Timestamp = BinaryPrimitives.ReadInt64LittleEndian(data);
                packet.Frame = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
                packet.Advantage = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
                ReadTiming(data[16..], packet);
                return true;
            case PacketKind.Pong:
                if (data.Length != 8)
                    return false;
                packet.Timestamp = BinaryPrimitives.ReadInt64LittleEndian(data);
                return true;
            case PacketKind.Checksum:
                if (data.Length != 12)
                    return false;
                packet.Frame = BinaryPrimitives.ReadInt32LittleEndian(data);
                packet.Checksum = BinaryPrimitives.ReadUInt64LittleEndian(data[4..]);
                return true;
            case PacketKind.ChecksumAck:
                if (data.Length != 4)
                    return false;
                packet.Frame = BinaryPrimitives.ReadInt32LittleEndian(data);
                return true;
            case PacketKind.Disconnect:
                if (data.Length != 1 || data[0] > 1)
                    return false;
                packet.ExcludeRemote = data[0] != 0;
                return true;
            default:
                return false;
        }
    }

    private static void WriteTiming(BinaryWriter writer, ProtocolPacket packet)
    {
        writer.Write(packet.TimingRevision);
        writer.Write(packet.ResponseDelay);
        writer.Write(packet.Donation);
        writer.Write(packet.ExtraDelay);
        writer.Write(packet.MaxExtraDelay);
    }

    private static void ReadTiming(ReadOnlySpan<byte> data, ProtocolPacket packet)
    {
        packet.TimingRevision = BinaryPrimitives.ReadInt32LittleEndian(data);
        packet.ResponseDelay = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        packet.Donation = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        packet.ExtraDelay = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        packet.MaxExtraDelay = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
    }
}
