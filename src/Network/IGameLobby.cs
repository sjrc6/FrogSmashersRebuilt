namespace FrogSmashers.Network;

public interface IGameLobby : IDisposable
{
    void Poll();
    bool Ready { get; }

    string Status { get; }

    string? Error { get; }

    int LocalPeer { get; }

    int[][] PeerSlots { get; }

    int[] PlayerTeams { get; }

    string MatchSettingsJson { get; }

    IPeerTransport CreateTransport();
}
