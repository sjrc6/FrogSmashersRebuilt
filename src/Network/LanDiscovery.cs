using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace FrogSmashers.Network;

internal static class LanDiscovery
{
    public const int Port = 24803;
    public const int MaximumPacket = 512;
    private const uint Magic = 0x31525346;

    public static byte[] Query(ulong nonce, string fingerprint) => Packet(1, nonce, fingerprint, null);

    public static byte[] Reply(
        ulong nonce,
        string fingerprint,
        Guid id,
        int port,
        string name,
        LobbyRoster roster,
        bool joinable
    )
    {
        int available = joinable ? roster.Slots.Count(s => s.Type == SlotType.Open && s.Player == null) : 0;
        return Packet(
            2,
            nonce,
            fingerprint,
            writer =>
            {
                writer.Write(id.ToByteArray());
                writer.Write((ushort)port);
                writer.Write((byte)roster.Count);
                writer.Write((byte)roster.Capacity);
                writer.Write((byte)available);
                writer.Write(LobbyListing.DisplayName(name, "LAN LOBBY"));
            }
        );
    }

    private static byte[] Packet(byte kind, ulong nonce, string fingerprint, Action<BinaryWriter>? payload)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(kind);
        writer.Write(nonce);
        writer.Write(fingerprint);
        payload?.Invoke(writer);
        return stream.ToArray();
    }

    public static bool ReadQuery(byte[] packet, string fingerprint, out ulong nonce)
    {
        nonce = 0;
        try
        {
            using var reader = Reader(packet);
            if (reader == null || reader.ReadUInt32() != Magic || reader.ReadByte() != 1)
                return false;
            nonce = reader.ReadUInt64();
            return reader.ReadString() == fingerprint && reader.BaseStream.Position == packet.Length;
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    public static LobbyListing? ReadReply(byte[] packet, IPEndPoint source, string fingerprint, ulong nonce)
    {
        try
        {
            using var reader = Reader(packet);
            if (
                reader == null
                || reader.ReadUInt32() != Magic
                || reader.ReadByte() != 2
                || reader.ReadUInt64() != nonce
                || reader.ReadString() != fingerprint
            )
                return null;
            string id = new Guid(reader.ReadBytes(16)).ToString("N");
            int port = reader.ReadUInt16();
            int players = reader.ReadByte();
            int capacity = reader.ReadByte();
            int available = reader.ReadByte();
            string name = reader.ReadString();
            if (
                port == 0
                || capacity is < 1 or > 8
                || players > capacity
                || available > capacity - players
                || name.Length > 24
                || reader.BaseStream.Position != packet.Length
            )
                return null;
            return new(
                id,
                $"udp:{source.Address}:{port}",
                LobbyListing.DisplayName(name, "LAN LOBBY"),
                players,
                capacity,
                available
            );
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static BinaryReader? Reader(byte[] data) =>
        data.Length is >= 14 and <= MaximumPacket ? new(new MemoryStream(data, false), Encoding.UTF8) : null;

    public static IPAddress Broadcast(IPAddress address, IPAddress mask)
    {
        byte[] bytes = address.GetAddressBytes();
        byte[] subnet = mask.GetAddressBytes();
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] |= (byte)~subnet[i];
        return new IPAddress(bytes);
    }

    public static IEnumerable<(IPAddress Address, IPAddress Broadcast)> Interfaces()
    {
        var result = new List<(IPAddress, IPAddress)>
        {
            (IPAddress.Any, IPAddress.Broadcast),
            (IPAddress.Loopback, IPAddress.Loopback),
        };
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return result;
        }
        foreach (var adapter in adapters)
        {
            if (
                adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback
            )
                continue;
            try
            {
                foreach (var address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (
                        address.Address.AddressFamily == AddressFamily.InterNetwork
                        && address.PrefixLength is > 0 and < 31
                    )
                        result.Add((address.Address, Broadcast(address.Address, address.IPv4Mask)));
                }
            }
            catch (NetworkInformationException) { }
        }
        return result.Distinct();
    }

    public static bool Transient(SocketException exception) =>
        exception.SocketErrorCode
            is SocketError.WouldBlock
                or SocketError.ConnectionReset
                or SocketError.MessageSize
                or SocketError.NoBufferSpaceAvailable;
}

internal sealed class LanDiscoveryHost : IDisposable
{
    private readonly UdpClient socket;
    private readonly string fingerprint;
    private readonly int gamePort;
    private readonly string name;
    private readonly Guid id = Guid.NewGuid();

    public LanDiscoveryHost(int gamePort, string fingerprint, string name, int discoveryPort = LanDiscovery.Port)
    {
        this.gamePort = gamePort;
        this.fingerprint = fingerprint;
        this.name = name;
        socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
            socket.Client.Blocking = false;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Poll(LobbyRoster roster, bool joinable)
    {
        for (int i = 0; i < 64; i++)
        {
            try
            {
                if (socket.Available == 0)
                    break;
                IPEndPoint source = new(IPAddress.Any, 0);
                var query = socket.Receive(ref source);
                if (!LanDiscovery.ReadQuery(query, fingerprint, out ulong nonce))
                    continue;
                byte[] reply = LanDiscovery.Reply(nonce, fingerprint, id, gamePort, name, roster, joinable);
                socket.Send(reply, reply.Length, source);
            }
            catch (SocketException)
            {
                break;
            }
        }
    }

    public void Dispose() => socket.Dispose();
}
