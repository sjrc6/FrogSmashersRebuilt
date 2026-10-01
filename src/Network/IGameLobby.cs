using FrogSmashers.Core;

namespace FrogSmashers.Network;

public interface IGameLobby : IDisposable
{
    void Poll();
    bool Connected { get; }
    bool IsHost { get; }
    bool Starting { get; }
    bool Ready { get; }
    int Generation { get; }
    string Status { get; }
    string? Error { get; }
    string? Notice { get; }
    int LocalPeer { get; }
    LobbyRoster Roster { get; }
    LobbyAccess Access { get; }
    int[][] PeerSlots { get; }
    int[] PlayerTeams { get; }
    int[] PlayerColors { get; }
    string MatchSettingsJson { get; }
    bool SetPlayers(LobbyPlayer[] players);
    bool EditSlot(int room, SlotType type);
    bool ApplySlotType(SlotType type);
    bool RemovePlayer(int peer, int id);
    bool SetSpectating(int peer, bool spectating);
    void Kick(int peer, bool ban);
    void SetMatchSettings(string settings);
    bool StartMatch(string settings);
    bool ReturnToLobby();
    void SendLobbyInputs(InputFrame[] inputs);
    InputFrame[] ReadLobbyInputs();
    void SendSnapshot(byte[] snapshot);
    byte[]? TakeSnapshot();
    IPeerTransport CreateTransport();
}
