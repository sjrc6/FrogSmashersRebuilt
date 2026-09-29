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

    public void Connect(string target, bool host, int expectedPeers, MatchSetup setup)
    {
        if (target.StartsWith("steam", StringComparison.Ordinal))
        {
            PrepareSteam();
        }

        CloseLobby();
        int[] teams = setup.Seats.Select(seat => seat.Team).ToArray();
        int playerCount = setup.Seats.Count;
        string matchSettings = JsonSerializer.Serialize(setup.Options);
        if (host)
        {
            Lobby = target switch
            {
                "udp" => UdpLobby.Host(
                    options.Port,
                    expectedPeers,
                    playerCount,
                    fingerprint,
                    matchSettings,
                    options.Lan,
                    localTeams: teams
                ),
                "steam" => SteamLobby.Host(expectedPeers, playerCount, fingerprint, matchSettings, localTeams: teams),
                _ => throw new ArgumentException("Host transport must be udp or steam"),
            };
        }
        else if (target.StartsWith("steam:", StringComparison.Ordinal))
        {
            Lobby = SteamLobby.Join(ulong.Parse(target[6..]), playerCount, fingerprint, localTeams: teams);
        }
        else if (target.StartsWith("udp:", StringComparison.Ordinal))
        {
            Lobby = UdpLobby.Join(target[4..], options.Port, playerCount, fingerprint, localTeams: teams);
        }
        else
        {
            throw new ArgumentException("Join address must start with udp: or steam:");
        }
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
