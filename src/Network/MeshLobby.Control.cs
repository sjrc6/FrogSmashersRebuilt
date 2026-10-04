using System.Buffers.Binary;
using System.Text.Json;

namespace FrogSmashers.Network;

internal sealed partial class MeshLobby
{
    private byte[] lastHello = [];
    private long lastHelloAt = -1000;

    private void SendHelloIfChanged()
    {
        var hello = new Control
        {
            Kind = ControlKind.Hello,
            Rollback = rollback,
            Hash = compatibility,
            Nonce = clientNonce,
            Players = requested,
            Spectating = requestedSpectating,
            AdmissionSecret = Connected ? "" : admissionSecret,
            CanSimulate = simulation != null,
            Version = requestVersion,
            Epoch = requestEpoch,
            Generation = Generation,
        };
        byte[] data = EncodeControl(hello);
        bool retry = (!Connected || LocalRequestPending) && Now - lastHelloAt >= 500;
        if (retry || !data.AsSpan().SequenceEqual(lastHello))
        {
            wire.Send(host!, data, true);
            lastHello = data;
            lastHelloAt = Now;
        }
        if (Connected)
            SendControl(
                host!,
                new Control
                {
                    Kind = ControlKind.Heartbeat,
                    Nonce = nonce,
                    Generation = Generation,
                    Mesh = ReadyLinks(),
                },
                false
            );
    }

    internal static byte[] EncodeControl(Control control)
    {
        if (control.Kind is ControlKind.Heartbeat or ControlKind.Mesh or ControlKind.MeshAcknowledged)
        {
            bool heartbeat = control.Kind == ControlKind.Heartbeat;
            byte[] data = new byte[heartbeat ? 28 : 23];
            BinaryPrimitives.WriteUInt32LittleEndian(data, Magic);
            data[4] = 3;
            data[5] = (byte)control.Kind;
            Guid.ParseExact(control.Nonce, "N").TryWriteBytes(data.AsSpan(6));
            if (heartbeat)
            {
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), control.Generation);
                ushort ready = 0;
                foreach (int peer in control.Mesh)
                    ready |= (ushort)(1 << peer);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), ready);
            }
            else
                data[22] = checked((byte)control.Peer);
            return data;
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(control);
        byte[] message = new byte[json.Length + 5];
        BinaryPrimitives.WriteUInt32LittleEndian(message, Magic);
        message[4] = 1;
        json.CopyTo(message, 5);
        return message;
    }

    internal static bool TryDecodeControl(ReadOnlySpan<byte> data, out Control? control)
    {
        control = null;
        if (data.Length is < 5 or > 8192 || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            return false;
        try
        {
            if (data[4] == 1)
            {
                control = JsonSerializer.Deserialize<Control>(data[5..]);
                return control != null && Valid(control);
            }
            if (data[4] != 3 || data.Length < 23)
                return false;
            ControlKind kind = (ControlKind)data[5];
            bool heartbeat = kind == ControlKind.Heartbeat;
            if (
                kind is not (ControlKind.Heartbeat or ControlKind.Mesh or ControlKind.MeshAcknowledged)
                || data.Length != (heartbeat ? 28 : 23)
            )
                return false;
            control = new Control { Kind = kind, Nonce = new Guid(data.Slice(6, 16)).ToString("N") };
            if (heartbeat)
            {
                control.Generation = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
                ushort ready = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]);
                if ((ready >> LobbyRoster.MaxPeers) != 0)
                    return false;
                control.Mesh = Enumerable
                    .Range(0, LobbyRoster.MaxPeers)
                    .Where(peer => (ready & (1 << peer)) != 0)
                    .ToArray();
            }
            else
                control.Peer = data[22];
            return control.Peer < LobbyRoster.MaxPeers && Valid(control);
        }
        catch (Exception exception)
            when (exception is JsonException or ArgumentException or InvalidDataException or OverflowException)
        {
            control = null;
            return false;
        }
    }
}
