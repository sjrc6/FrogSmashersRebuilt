using System.Net;
using System.Net.Sockets;

namespace FrogSmashers.Network;

public sealed class UdpLobby : IGameLobby
{
    private readonly RelayLobby lobby;

    public static UdpLobby Host(
        int port,
        int peerCount,
        int localPlayers,
        string contentHash,
        string settingsJson,
        bool allowLan = false,
        int[]? localTeams = null
    ) =>
        new(
            new UdpWire(new IPEndPoint(allowLan ? IPAddress.Any : IPAddress.Loopback, port)),
            null,
            peerCount,
            localPlayers,
            contentHash,
            settingsJson,
            localTeams
        );

    public static UdpLobby Join(
        string hostname,
        int port,
        int localPlayers,
        string contentHash,
        int[]? localTeams = null
    )
    {
        var ip = Dns.GetHostAddresses(hostname).First(x => x.AddressFamily == AddressFamily.InterNetwork);
        return new(
            new UdpWire(new IPEndPoint(IPAddress.Any, 0)),
            new IPEndPoint(ip, port).ToString(),
            0,
            localPlayers,
            contentHash,
            "",
            localTeams
        );
    }

    private UdpLobby(
        IWire wire,
        string? host,
        int peers,
        int localPlayers,
        string hash,
        string settings,
        int[]? localTeams
    )
    {
        try
        {
            lobby = new RelayLobby(wire, host, peers, localPlayers, hash, settings, localTeams);
        }
        catch
        {
            wire.Dispose();
            throw;
        }
    }

    public void Poll() => lobby.Poll();

    public bool Ready => lobby.Ready;
    public string Status => lobby.Status;
    public string? Error => lobby.Error;
    public int LocalPeer => lobby.LocalPeer;
    public int[][] PeerSlots => lobby.PeerSlots;
    public int[] PlayerTeams => lobby.PlayerTeams;
    public string MatchSettingsJson => lobby.MatchSettingsJson;

    public IPeerTransport CreateTransport() => lobby.CreateTransport();

    public void Dispose() => lobby.Dispose();
}
