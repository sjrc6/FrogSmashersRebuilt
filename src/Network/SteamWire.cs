using System.Diagnostics;
using System.Runtime.InteropServices;
using Steamworks;

namespace FrogSmashers.Network;

internal sealed class SteamWire : IWire
{
    private const int VirtualPort = 730;
    private readonly CSteamID lobby;
    private readonly bool hosting;
    private readonly ulong localId;
    private readonly HSteamListenSocket listener;
    private readonly Callback<SteamNetConnectionStatusChangedCallback_t> callback;
    private readonly Dictionary<ulong, HSteamNetConnection> connections = new();
    private readonly Dictionary<ulong, long> attempts = new();
    private readonly Dictionary<ulong, Queue<byte[]>> pending = new();
    private readonly HashSet<ulong> approved = new();
    private readonly HashSet<ulong> connected = new();
    private readonly Queue<WireMessage> incoming = new();
    private readonly Queue<string> disconnected = new();
    private readonly IntPtr[] receivePointers = new IntPtr[32];
    private long sent;
    private long received;
    private long ignored;
    private long backpressure;
    private bool disposed;

    public CSteamID Owner { get; }
    public string? Error { get; private set; }
    public string LocalAddress => localId.ToString();
    public long TimeMilliseconds => Stopwatch.GetElapsedTime(0).Ticks / TimeSpan.TicksPerMillisecond;
    public WireStatistics Statistics => new(sent, received, 0, ignored, backpressure);

    public SteamWire(CSteamID lobby, bool hosting, CSteamID owner)
    {
        this.lobby = lobby;
        this.hosting = hosting;
        localId = SteamUser.GetSteamID().m_SteamID;
        Owner = owner;
        callback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionChanged);
        listener = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, []);
        if (listener == HSteamListenSocket.Invalid)
        {
            Error = "Steam P2P listen socket could not be created";
            return;
        }

        if (!hosting)
        {
            approved.Add(owner.m_SteamID);
            Connect(owner.m_SteamID);
        }
    }

    public void SetPeers(IReadOnlyCollection<string> addresses)
    {
        var next = new HashSet<ulong>();
        foreach (string address in addresses)
        {
            if (ulong.TryParse(address, out ulong id) && id != localId)
                next.Add(id);
        }
        if (!hosting)
            next.Add(Owner.m_SteamID);
        foreach (ulong removed in approved.Except(next).ToArray())
            Close(removed, "Peer left lobby");
        approved.Clear();
        approved.UnionWith(next);
        ConnectApprovedPeers();
    }

    public bool IsConnected(string address) => ulong.TryParse(address, out ulong id) && connected.Contains(id);

    public bool TakeDisconnected(out string address) => disconnected.TryDequeue(out address!);

    public bool Receive(out WireMessage message) => incoming.TryDequeue(out message);

    private bool IsMember(ulong id)
    {
        for (int n = 0; n < SteamMatchmaking.GetNumLobbyMembers(lobby); n++)
        {
            if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, n).m_SteamID == id)
                return true;
        }
        return false;
    }

    private void ConnectApprovedPeers()
    {
        foreach (ulong id in approved)
        {
            bool initiates = id == Owner.m_SteamID || (!hosting && localId < id);
            if (initiates && !connections.ContainsKey(id) && IsMember(id))
                Connect(id);
        }
    }

    private void Connect(ulong id)
    {
        long now = TimeMilliseconds;
        if (attempts.TryGetValue(id, out long lastAttempt) && now - lastAttempt < 500)
            return;
        attempts[id] = now;
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(new CSteamID(id));
        var connection = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, 0, []);
        if (connection != HSteamNetConnection.Invalid)
            connections[id] = connection;
    }

    private void OnConnectionChanged(SteamNetConnectionStatusChangedCallback_t change)
    {
        ulong id = change.m_info.m_identityRemote.GetSteamID64();
        if (
            change.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting
            && change.m_info.m_hListenSocket == listener
        )
        {
            if (id == localId || !IsMember(id) || (!hosting && !approved.Contains(id)) || connections.ContainsKey(id))
            {
                SteamNetworkingSockets.CloseConnection(
                    change.m_hConn,
                    0,
                    "Peer is not admitted or is already connected",
                    false
                );
                return;
            }
            var result = SteamNetworkingSockets.AcceptConnection(change.m_hConn);
            if (result != EResult.k_EResultOK)
            {
                SteamNetworkingSockets.CloseConnection(change.m_hConn, 0, "Could not accept peer", false);
                return;
            }
            connections[id] = change.m_hConn;
        }

        if (!connections.TryGetValue(id, out var known) || known != change.m_hConn)
            return;
        if (change.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
            connected.Add(id);
        if (
            change.m_info.m_eState
            is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally
        )
        {
            bool established = connected.Contains(id);
            Close(id, "Closing failed connection");
            if (established)
                disconnected.Enqueue(id.ToString());
            if (!hosting && id == Owner.m_SteamID)
                Error = $"Steam host disconnected: {change.m_info.m_szEndDebug}";
        }
    }

    public void Poll()
    {
        if (disposed)
            return;
        SteamAPI.RunCallbacks();
        ConnectApprovedPeers();
        int budget = 2048;
        foreach (var (id, connection) in connections.ToArray())
        {
            Flush(id);
            while (budget > 0)
            {
                int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(
                    connection,
                    receivePointers,
                    Math.Min(receivePointers.Length, budget)
                );
                if (count <= 0)
                    break;
                budget -= count;
                for (int n = 0; n < count; n++)
                {
                    try
                    {
                        var message = SteamNetworkingMessage_t.FromIntPtr(receivePointers[n]);
                        if (message.m_cbSize is > 0 and <= 8192 && incoming.Count < 4096)
                        {
                            var data = new byte[message.m_cbSize];
                            Marshal.Copy(message.m_pData, data, 0, data.Length);
                            incoming.Enqueue(new WireMessage(id.ToString(), data));
                            received++;
                        }
                        else
                            ignored++;
                    }
                    finally
                    {
                        SteamNetworkingMessage_t.Release(receivePointers[n]);
                    }
                }
                if (count < receivePointers.Length)
                    break;
            }
        }
    }

    public void Send(string address, byte[] data, bool reliable)
    {
        if (data.Length is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(data));
        if (!ulong.TryParse(address, out ulong id) || (!approved.Contains(id) && !connections.ContainsKey(id)))
        {
            ignored++;
            return;
        }
        if (!reliable)
        {
            TrySend(id, data, false);
            return;
        }
        if (!pending.TryGetValue(id, out var queue))
        {
            queue = new Queue<byte[]>();
            pending.Add(id, queue);
        }
        if (queue.Count >= 512)
        {
            backpressure++;
            Fail(id, "Reliable send queue filled");
            return;
        }
        queue.Enqueue(data.ToArray());
        Flush(id);
    }

    private void Flush(ulong id)
    {
        if (!pending.TryGetValue(id, out var queue))
            return;
        while (queue.TryPeek(out byte[]? data) && TrySend(id, data, true))
            queue.Dequeue();
    }

    private bool TrySend(ulong id, byte[] data, bool reliable)
    {
        if (!connected.Contains(id) || !connections.TryGetValue(id, out var connection))
            return false;
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            int flags = reliable
                ? Constants.k_nSteamNetworkingSend_Reliable
                : Constants.k_nSteamNetworkingSend_UnreliableNoDelay;
            var result = SteamNetworkingSockets.SendMessageToConnection(
                connection,
                pinned.AddrOfPinnedObject(),
                (uint)data.Length,
                flags,
                out _
            );
            if (result == EResult.k_EResultOK)
            {
                sent++;
                return true;
            }
            if (result == EResult.k_EResultLimitExceeded)
                backpressure++;
            else if (result == EResult.k_EResultIgnored)
                ignored++;
            else
                Fail(id, $"Steam send failed: {result}");
            return false;
        }
        finally
        {
            pinned.Free();
        }
    }

    private void Fail(ulong id, string reason)
    {
        Close(id, reason);
        disconnected.Enqueue(id.ToString());
        if (!hosting && id == Owner.m_SteamID)
            Error = reason;
    }

    private void Close(ulong id, string reason)
    {
        connected.Remove(id);
        pending.Remove(id);
        if (connections.Remove(id, out var connection))
            SteamNetworkingSockets.CloseConnection(connection, 0, reason, false);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (ulong id in connections.Keys.ToArray())
            Close(id, "Lobby closed");
        if (listener != HSteamListenSocket.Invalid)
            SteamNetworkingSockets.CloseListenSocket(listener);
        callback.Dispose();
    }
}
