using System.Text.Json;
using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed class OnlineController : IDisposable
{
    private readonly LaunchOptions options;
    private readonly string fingerprint;
    private SteamClient? steam;
    public IGameLobby? Lobby { get; private set; }

    public OnlineController(LaunchOptions options, string contentHash)
    {
        this.options = options;
        fingerprint = NetworkBuild.ContentFingerprint(contentHash);
    }

    public void PrepareSteam()
    {
        if (steam?.Available == true)
        {
            return;
        }

        steam?.Dispose();
        steam = SteamClient.Connect();
    }

    public void Connect(
        string target,
        bool host,
        int capacity,
        LobbyRoster localRoster,
        MatchOptions match,
        bool allowLan,
        int joinDevice
    )
    {
        if (target.StartsWith("steam", StringComparison.Ordinal))
            PrepareSteam();
        CloseLobby();
        var players = localRoster.Players(0).Where(player => host || !player.Cpu).ToArray();
        if (!host && players.Length == 0)
            players = [new LobbyPlayer(joinDevice)];
        string settings = JsonSerializer.Serialize(match);
        if (host)
            Lobby = target switch
            {
                "udp" => UdpLobby.Host(
                    options.Port,
                    capacity,
                    players,
                    fingerprint,
                    settings,
                    allowLan,
                    localRoster.Slots
                ),
                "steam" => SteamLobby.Host(capacity, players, fingerprint, settings, initialRooms: localRoster.Slots),
                _ => throw new ArgumentException("Host transport must be udp or steam"),
            };
        else if (target.StartsWith("steam:", StringComparison.Ordinal))
            Lobby = SteamLobby.Join(ulong.Parse(target[6..]), players, fingerprint);
        else if (target.StartsWith("udp:", StringComparison.Ordinal))
            Lobby = UdpLobby.Join(target[4..], options.Port, players, fingerprint);
        else
            throw new ArgumentException("Join address must start with udp: or steam:");
    }

    public ulong PollInvites()
    {
        steam?.Poll();
        ulong requested = steam?.ConsumeRequestedLobby() ?? 0;
        if (requested == 0 && Lobby is SteamLobby pending && pending.RequestedLobby != pending.LobbyCode)
        {
            requested = pending.RequestedLobby;
        }

        return Lobby is SteamLobby current && current.LobbyCode == requested ? 0 : requested;
    }

    public void CloseLobby()
    {
        Lobby?.Dispose();
        Lobby = null;
    }

    public void Dispose()
    {
        CloseLobby();
        steam?.Dispose();
    }
}
