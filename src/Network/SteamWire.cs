using System.Runtime.InteropServices;
using Steamworks;

namespace FrogSmashers.Network;

internal sealed class SteamWire : IWire
{
    private const int VirtualPort = 730;
    private readonly CSteamID lobby;
    private readonly bool hosting;
    private readonly HSteamListenSocket listener;
    private readonly Callback<SteamNetConnectionStatusChangedCallback_t> callback;
    private readonly Dictionary<ulong, HSteamNetConnection> connections = new();
    private readonly HashSet<ulong> connected = new();
    private readonly Queue<WireMessage> incoming = new();
    private readonly Queue<string> disconnected = new();
    private bool disposed;
    public CSteamID Owner { get; }
    public string? Error { get; private set; }

    public SteamWire(CSteamID lobby, bool hosting, CSteamID owner)
    {
        this.lobby = lobby;
        this.hosting = hosting;
        Owner = owner;
        callback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionChanged);
        if (hosting)
        {
            listener = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, []);
            if (listener == HSteamListenSocket.Invalid)
            {
                Error = "Steam P2P listen socket could not be created";
            }
        }
        else
        {
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(owner);
            var connection = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, 0, []);
            if (connection == HSteamNetConnection.Invalid)
            {
                Error = "Steam P2P connection could not be created";
            }
            else
            {
                connections[owner.m_SteamID] = connection;
            }
        }
    }

    private bool IsMember(ulong id)
    {
        for (int n = 0; n < SteamMatchmaking.GetNumLobbyMembers(lobby); n++)
        {
            if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, n).m_SteamID == id)
            {
                return true;
            }
        }

        return false;
    }

    private void OnConnectionChanged(SteamNetConnectionStatusChangedCallback_t c)
    {
        ulong id = c.m_info.m_identityRemote.GetSteamID64();
        if (
            c.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting
            && hosting
        )
        {
            if (!IsMember(id) || c.m_info.m_hListenSocket != listener || connections.ContainsKey(id))
            {
                SteamNetworkingSockets.CloseConnection(
                    c.m_hConn,
                    0,
                    "Unknown lobby member or duplicate connection",
                    false
                );
                return;
            }

            var result = SteamNetworkingSockets.AcceptConnection(c.m_hConn);
            if (result != EResult.k_EResultOK)
            {
                Error = $"Steam could not accept peer: {result}";
                return;
            }

            connections[id] = c.m_hConn;
        }

        if (!connections.TryGetValue(id, out var known) || known != c.m_hConn)
        {
            return;
        }

        if (c.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
        {
            connected.Add(id);
        }

        if (
            c.m_info.m_eState
            is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally
        )
        {
            connected.Remove(id);
            connections.Remove(id);
            SteamNetworkingSockets.CloseConnection(c.m_hConn, 0, "Closing failed connection", false);
            if (hosting)
                disconnected.Enqueue(id.ToString());
            else
                Error = $"Steam peer disconnected: {c.m_info.m_szEndDebug}";
        }
    }

    public bool TakeDisconnected(out string address) => disconnected.TryDequeue(out address!);

    public void Poll()
    {
        if (disposed)
        {
            return;
        }

        SteamAPI.RunCallbacks();
        var pointers = new IntPtr[32];
        int budget = 2048;
        foreach (var pair in connections.ToArray())
        {
            while (budget > 0)
            {
                int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(
                    pair.Value,
                    pointers,
                    Math.Min(pointers.Length, budget)
                );
                if (count <= 0)
                {
                    break;
                }

                budget -= count;
                for (int n = 0; n < count; n++)
                {
                    try
                    {
                        var msg = SteamNetworkingMessage_t.FromIntPtr(pointers[n]);
                        if (msg.m_cbSize is > 0 and <= 8192)
                        {
                            var data = new byte[msg.m_cbSize];
                            Marshal.Copy(msg.m_pData, data, 0, data.Length);
                            incoming.Enqueue(new WireMessage(pair.Key.ToString(), data));
                        }
                    }
                    finally
                    {
                        SteamNetworkingMessage_t.Release(pointers[n]);
                    }
                }

                if (count < pointers.Length)
                {
                    break;
                }
            }
        }
    }

    public void Send(string address, byte[] data, bool reliable)
    {
        if (
            !ulong.TryParse(address, out var id)
            || !connected.Contains(id)
            || !connections.TryGetValue(id, out var connection)
        )
        {
            return;
        }

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
            if (result is not (EResult.k_EResultOK or EResult.k_EResultIgnored or EResult.k_EResultLimitExceeded))
            {
                Error = $"Steam send failed: {result}";
            }
        }
        finally
        {
            pinned.Free();
        }
    }

    public bool Receive(out WireMessage message) => incoming.TryDequeue(out message);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var connection in connections.Values)
        {
            SteamNetworkingSockets.CloseConnection(connection, 0, "Match closed", false);
        }

        if (listener != HSteamListenSocket.Invalid)
        {
            SteamNetworkingSockets.CloseListenSocket(listener);
        }

        callback.Dispose();
    }
}
