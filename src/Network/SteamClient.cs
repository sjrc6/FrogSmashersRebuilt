using Steamworks;

namespace FrogSmashers.Network;

public sealed class SteamClient : IDisposable
{
    private readonly Callback<GameLobbyJoinRequested_t>? inviteCallback;
    private bool acquired;
    private bool disposed;
    public bool Available => acquired && !disposed;
    public string? Error { get; }
    public ulong RequestedLobby { get; private set; }

    public static SteamClient Connect(uint appId = 480) => new(appId);

    private SteamClient(uint appId)
    {
        acquired = SteamRuntime.TryAcquire(appId, out var error);
        Error = error;
        if (acquired)
        {
            inviteCallback = Callback<GameLobbyJoinRequested_t>.Create(c =>
                RequestedLobby = c.m_steamIDLobby.m_SteamID
            );
        }
    }

    public void Poll()
    {
        if (Available)
        {
            SteamAPI.RunCallbacks();
        }
    }

    public ulong ConsumeRequestedLobby()
    {
        ulong requested = RequestedLobby;
        RequestedLobby = 0;
        return requested;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        inviteCallback?.Dispose();
        if (acquired)
        {
            acquired = false;
            SteamRuntime.Release();
        }
    }
}
