using System.Net;
using System.Net.Sockets;

namespace FrogSmashers.Network;

public sealed class UdpLobby : GameLobby
{
    private readonly MeshLobby lobby;
    protected override IGameLobby Active => lobby;
    public override bool IsHost => lobby.IsHost;

    public static UdpLobby Host(
        int port,
        int capacity,
        LobbyPlayer[] players,
        string contentHash,
        string settingsJson,
        bool allowLan = false,
        IReadOnlyList<LobbySlot>? initialRooms = null
    ) =>
        new(
            new UdpWire(new IPEndPoint(allowLan ? IPAddress.Any : IPAddress.Loopback, port)),
            null,
            capacity,
            players,
            contentHash,
            settingsJson,
            initialRooms
        );

    public static UdpLobby Join(string hostname, int port, LobbyPlayer[] players, string contentHash)
    {
        var ip = Dns.GetHostAddresses(hostname).First(address => address.AddressFamily == AddressFamily.InterNetwork);
        return new(
            new UdpWire(new IPEndPoint(IPAddress.Any, 0)),
            new IPEndPoint(ip, port).ToString(),
            8,
            players,
            contentHash,
            ""
        );
    }

    private UdpLobby(
        IWire wire,
        string? host,
        int capacity,
        LobbyPlayer[] players,
        string hash,
        string settings,
        IReadOnlyList<LobbySlot>? initialRooms = null
    )
    {
        try
        {
            lobby = new MeshLobby(wire, host, capacity, players, hash, settings, initialRooms);
        }
        catch
        {
            wire.Dispose();
            throw;
        }
    }

    public override void Poll() => lobby.Poll();

    public override IPeerTransport CreateTransport() => lobby.CreateTransport();

    public override void Dispose() => lobby.Dispose();
}
