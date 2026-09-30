using FrogSmashers.Core;

namespace FrogSmashers.Network;

public interface IGameLobby : IDisposable
{
    void Poll();
    bool Connected { get; }
    bool IsHost { get; }
    bool Starting { get; }
    bool Ready { get; }
    string Status { get; }
    string? Error { get; }
    string? Notice { get; }
    int LocalPeer { get; }
    LobbyRoster Roster { get; }
    int[][] PeerSlots { get; }
    int[] PlayerTeams { get; }
    int[] PlayerColors { get; }
    string MatchSettingsJson { get; }
    bool SetPlayers(LobbyPlayer[] players);
    bool EditSlot(int room, SlotType type, bool open, bool remove = false);
    void Kick(int peer, bool ban);
    void SetMatchSettings(string settings);
    bool StartMatch(string settings);
    void SendLobbyInputs(InputFrame[] inputs);
    InputFrame[] ReadLobbyInputs();
    void SendSnapshot(byte[] snapshot);
    byte[]? TakeSnapshot();
    IPeerTransport CreateTransport();
}
