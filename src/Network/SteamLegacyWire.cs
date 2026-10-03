using Steamworks;

namespace FrogSmashers.Network;

internal sealed class SteamLegacyWire : IWire
{
    private sealed class Peer
    {
        public readonly Queue<byte[]> Pending = new();
        public bool Connected;
    }

    private readonly ISteamLegacyApi api;
    private readonly bool hosting;
    private readonly ulong owner;
    private readonly HashSet<ulong> approved = new();
    private readonly Dictionary<ulong, Peer> peers = new();
    private readonly Queue<WireMessage> incoming = new();
    private readonly Queue<string> disconnected = new();
    private long sent;
    private long received;
    private long ignored;
    private long backpressure;
    private bool disposed;

    public string? Error { get; private set; }
    public string LocalAddress => api.LocalId.ToString();
    public long TimeMilliseconds => api.TimeMilliseconds;
    public WireStatistics Statistics => new(sent, received, 0, ignored, backpressure);

    public SteamLegacyWire(ISteamLegacyApi api, bool hosting, ulong owner)
    {
        this.api = api;
        this.hosting = hosting;
        this.owner = owner;
        api.SessionRequested += OnSessionRequested;
        api.SessionFailed += OnSessionFailed;
        if (!hosting)
            approved.Add(owner);
    }

    public void SetPeers(IReadOnlyCollection<string> addresses)
    {
        var next = new HashSet<ulong>();
        foreach (string address in addresses)
            if (ulong.TryParse(address, out ulong id) && id != 0 && id != api.LocalId)
                next.Add(id);
        if (!hosting)
            next.Add(owner);
        foreach (ulong id in approved.Except(next).ToArray())
            Close(id);
        approved.Clear();
        approved.UnionWith(next);
    }

    private bool Allowed(ulong id) =>
        id != 0 && id != api.LocalId && (hosting || approved.Contains(id)) && api.IsMember(id);

    private Peer GetPeer(ulong id)
    {
        if (!peers.TryGetValue(id, out var peer))
            peers.Add(id, peer = new Peer());
        return peer;
    }

    private void OnSessionRequested(ulong id)
    {
        if (Allowed(id) && api.Accept(id))
            GetPeer(id);
        else
            api.Close(id);
    }

    private void OnSessionFailed(ulong id, EP2PSessionError reason)
    {
        if (peers.ContainsKey(id))
            Fail(id, $"Steam P2P session failed: {reason}");
    }

    public bool IsConnected(string address) =>
        ulong.TryParse(address, out ulong id) && peers.TryGetValue(id, out var peer) && peer.Connected;

    public bool TakeDisconnected(out string address) => disconnected.TryDequeue(out address!);

    public bool Receive(out WireMessage message) => incoming.TryDequeue(out message);

    public void Poll()
    {
        if (disposed)
            return;
        api.Poll();
        for (int n = 0; n < 2048 && api.Receive(out ulong source, out byte[] data); n++)
        {
            if (!Allowed(source) || data.Length is < 1 or > 8192 || incoming.Count >= 4096)
            {
                ignored++;
                continue;
            }
            var peer = GetPeer(source);
            peer.Connected = true;
            received++;
            incoming.Enqueue(new(source.ToString(), data));
        }
        foreach (var (id, peer) in peers)
            Flush(id, peer);
    }

    public void Send(string address, byte[] data, bool reliable)
    {
        if (data.Length is < 1 or > 8192 || (!reliable && data.Length > 1200))
            throw new ArgumentOutOfRangeException(nameof(data));
        if (disposed || !ulong.TryParse(address, out ulong id) || !Allowed(id))
        {
            ignored++;
            return;
        }
        var peer = GetPeer(id);
        if (!reliable)
        {
            TrySend(id, data, EP2PSend.k_EP2PSendUnreliable);
            return;
        }
        if (peer.Pending.Count >= 512)
        {
            backpressure++;
            Fail(id, "Reliable send queue filled");
            return;
        }
        peer.Pending.Enqueue(data.ToArray());
        Flush(id, peer);
    }

    private void Flush(ulong id, Peer peer)
    {
        while (peer.Pending.TryPeek(out var data) && TrySend(id, data, EP2PSend.k_EP2PSendReliable))
            peer.Pending.Dequeue();
    }

    private bool TrySend(ulong id, byte[] data, EP2PSend mode)
    {
        if (!api.Send(id, data, mode))
        {
            backpressure++;
            return false;
        }
        sent++;
        return true;
    }

    private void Fail(ulong id, string reason)
    {
        Console.Error.WriteLine($"Steam peer {id}: {reason}");
        Close(id);
        disconnected.Enqueue(id.ToString());
        if (!hosting && id == owner)
            Error = reason;
    }

    private void Close(ulong id)
    {
        if (peers.Remove(id))
            api.Close(id);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (var (id, peer) in peers.ToArray())
        {
            Flush(id, peer);
            Close(id);
        }
        api.SessionRequested -= OnSessionRequested;
        api.SessionFailed -= OnSessionFailed;
        api.Dispose();
        incoming.Clear();
        disconnected.Clear();
    }
}
