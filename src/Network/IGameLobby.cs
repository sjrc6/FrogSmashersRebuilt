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
    IReadOnlyList<LobbyPlayer> PendingLocalPlayers { get; }
    bool PendingLocalSpectating { get; }
    bool LocalRequestPending { get; }
    LobbyAccess Access { get; }
    int[][] PeerSlots { get; }
    int[] InputPlayerSlots { get; }
    LobbyCpuCommand HostCommand { get; }
    int[] PlayerTeams { get; }
    int[] PlayerColors { get; }
    string MatchSettingsJson { get; }
    bool SetPlayers(LobbyPlayer[] players);
    bool EditSlot(int room, SlotType type);
    SlotType GetSlotType(int room);
    bool IsSlotEditPending(int room);
    bool ApplySlotType(SlotType type);
    bool RemovePlayer(int peer, int id);
    bool SetSpectating(int peer, bool spectating);
    void Kick(int peer, bool ban);
    void SetMatchSettings(string settings);
    bool StartMatch(string settings);
    bool ReturnToLobby();
    ulong SessionId { get; }
    IReadOnlyList<int> PeerIds { get; }
    NetworkSession? LobbySession { get; }
    bool SimulationReady { get; }
    bool Transitioning { get; }
    RollbackPreferences RollbackSettings { get; }
    IReadOnlyDictionary<int, RollbackPreferences> PeerRollbackSettings { get; }
    void SetRollbackSettings(RollbackPreferences preferences);
    void AttachSimulation(LobbySimulation simulation);
    IPeerTransport CreateTransport();
}
