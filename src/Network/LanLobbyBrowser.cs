using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace FrogSmashers.Network;

public sealed class LanLobbyBrowser : ILobbyBrowser
{
    private readonly string fingerprint;
    private readonly int partySize;
    private readonly int discoveryPort;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<(UdpClient Socket, IPEndPoint Target)> probes = new();
    private readonly Dictionary<string, (LobbyListing Lobby, long Seen)> found = new();
    private ulong nonce;
    private long nextQuery;
    private long started;
    public IReadOnlyList<LobbyListing> Results { get; private set; } = [];
    public bool Searching { get; private set; }
    public string? Error { get; private set; }

    public LanLobbyBrowser(string fingerprint, int partySize, int discoveryPort = LanDiscovery.Port)
    {
        this.fingerprint = fingerprint;
        this.partySize = partySize;
        this.discoveryPort = discoveryPort;
        Refresh();
    }

    public void Refresh()
    {
        CloseSockets();
        found.Clear();
        Results = [];
        Error = null;
        nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
        started = nextQuery = clock.ElapsedMilliseconds;
        foreach (var (address, broadcast) in LanDiscovery.Interfaces())
        {
            var socket = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                socket.Client.Bind(new IPEndPoint(address, 0));
                socket.Client.Blocking = false;
                socket.EnableBroadcast = true;
                probes.Add((socket, new IPEndPoint(broadcast, discoveryPort)));
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }
        Searching = probes.Count > 0;
        if (!Searching)
            Error = "LAN SEARCH UNAVAILABLE";
    }

    public void Poll()
    {
        long now = clock.ElapsedMilliseconds;
        bool changed = false;
        if (now >= nextQuery)
        {
            byte[] query = LanDiscovery.Query(nonce);
            foreach (var (socket, target) in probes)
            {
                try
                {
                    socket.Send(query, query.Length, target);
                }
                catch (SocketException) { }
            }
            nextQuery = now + 1000;
        }
        foreach (var (socket, _) in probes)
        {
            for (int i = 0; i < 64; i++)
            {
                try
                {
                    if (socket.Available == 0)
                        break;
                    IPEndPoint source = new(IPAddress.Any, 0);
                    byte[] packet = socket.Receive(ref source);
                    var listing = LanDiscovery.ReadReply(packet, source, fingerprint, nonce);
                    if (listing == null)
                        continue;
                    if (listing.AvailableSlots < partySize)
                    {
                        changed |= found.Remove(listing.Id);
                        continue;
                    }
                    found.TryGetValue(listing.Id, out var previous);
                    if (found.Count < 128 || found.ContainsKey(listing.Id))
                    {
                        changed |= listing != previous.Lobby;
                        found[listing.Id] = (listing, now);
                    }
                }
                catch (SocketException ex)
                {
                    if (!LanDiscovery.Transient(ex))
                        Error = "LAN SEARCH UNAVAILABLE";
                    break;
                }
            }
        }
        foreach (var id in found.Where(pair => now - pair.Value.Seen > 5000).Select(pair => pair.Key).ToArray())
            changed |= found.Remove(id);
        if (changed)
            Results = found
                .Values.Select(item => item.Lobby)
                .OrderBy(lobby => lobby.Name)
                .ThenBy(lobby => lobby.Id)
                .ToArray();
        Searching = probes.Count > 0 && now - started < 3000;
    }

    private void CloseSockets()
    {
        foreach (var (socket, _) in probes)
            socket.Dispose();
        probes.Clear();
    }

    public void Dispose() => CloseSockets();
}
