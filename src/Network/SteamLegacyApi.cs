using System.Diagnostics;
using Steamworks;

namespace FrogSmashers.Network;

internal interface ISteamLegacyApi : IDisposable
{
    ulong LocalId { get; }
    long TimeMilliseconds { get; }
    event Action<ulong>? SessionRequested;
    event Action<ulong, EP2PSessionError>? SessionFailed;
    bool IsMember(ulong id);
    void Poll();
    bool Accept(ulong id);
    void Close(ulong id);
    bool Send(ulong id, byte[] data, EP2PSend mode);
    bool Receive(out ulong source, out byte[] data);
}

internal sealed class SteamLegacyApi : ISteamLegacyApi
{
    private const int Channel = 730;
    private readonly CSteamID lobby;
    private readonly Callback<P2PSessionRequest_t> requested;
    private readonly Callback<P2PSessionConnectFail_t> failed;
    private readonly byte[] receiveBuffer = new byte[1024 * 1024];

    public ulong LocalId { get; } = SteamUser.GetSteamID().m_SteamID;
    public long TimeMilliseconds => Stopwatch.GetElapsedTime(0).Ticks / TimeSpan.TicksPerMillisecond;
    public event Action<ulong>? SessionRequested;
    public event Action<ulong, EP2PSessionError>? SessionFailed;

    public SteamLegacyApi(CSteamID lobby)
    {
        this.lobby = lobby;
        requested = Callback<P2PSessionRequest_t>.Create(c => SessionRequested?.Invoke(c.m_steamIDRemote.m_SteamID));
        failed = Callback<P2PSessionConnectFail_t>.Create(c =>
            SessionFailed?.Invoke(c.m_steamIDRemote.m_SteamID, (EP2PSessionError)c.m_eP2PSessionError)
        );
        SteamNetworking.AllowP2PPacketRelay(true);
    }

    public bool IsMember(ulong id)
    {
        for (int n = 0; n < SteamMatchmaking.GetNumLobbyMembers(lobby); n++)
            if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, n).m_SteamID == id)
                return true;
        return false;
    }

    public void Poll() => SteamAPI.RunCallbacks();

    public bool Accept(ulong id) => SteamNetworking.AcceptP2PSessionWithUser(new CSteamID(id));

    public void Close(ulong id) => SteamNetworking.CloseP2PSessionWithUser(new CSteamID(id));

    public bool Send(ulong id, byte[] data, EP2PSend mode) =>
        SteamNetworking.SendP2PPacket(new CSteamID(id), data, (uint)data.Length, mode, Channel);

    public bool Receive(out ulong source, out byte[] data)
    {
        source = 0;
        data = [];
        if (!SteamNetworking.IsP2PPacketAvailable(out uint expectedSize, Channel))
            return false;
        if (
            !SteamNetworking.ReadP2PPacket(
                receiveBuffer,
                (uint)receiveBuffer.Length,
                out uint size,
                out var remote,
                Channel
            )
        )
            return false;
        source = remote.m_SteamID;
        if (size == expectedSize && size is > 0 and <= 8192)
            data = receiveBuffer.AsSpan(0, (int)size).ToArray();
        return true;
    }

    public void Dispose()
    {
        requested.Dispose();
        failed.Dispose();
    }
}
