using FrogSmashers.Core;

namespace FrogSmashers.Network;

public abstract class GameLobby : IGameLobby
{
    protected abstract IGameLobby? Active { get; }
    private readonly LobbyRoster emptyRoster = new();
    public bool Connected => Active?.Connected ?? false;
    public bool Starting => Active?.Starting ?? false;
    public bool Ready => Active?.Ready ?? false;
    public int Generation => Active?.Generation ?? 0;
    public abstract bool IsHost { get; }
    public virtual string Status => Active?.Status ?? "Connecting";
    public virtual string? Error => Active?.Error;
    public string? Notice => Active?.Notice;
    public int LocalPeer => Active?.LocalPeer ?? -1;
    public LobbyRoster Roster => Active?.Roster ?? emptyRoster;
    public IReadOnlyList<LobbyPlayer> PendingLocalPlayers => Active?.PendingLocalPlayers ?? [];
    public bool PendingLocalSpectating => Active?.PendingLocalSpectating ?? false;
    public bool LocalRequestPending => Active?.LocalRequestPending ?? false;
    public LobbyAccess Access => Active?.Access ?? default;
    public int[][] PeerSlots => Active?.PeerSlots ?? [];
    public int[] InputPlayerSlots => Active?.InputPlayerSlots ?? [];
    public LobbyCpuCommand HostCommand => Active?.HostCommand ?? default;
    public int[] PlayerTeams => Active?.PlayerTeams ?? [];
    public int[] PlayerColors => Active?.PlayerColors ?? [];
    public string MatchSettingsJson => Active?.MatchSettingsJson ?? "";

    public bool SetPlayers(LobbyPlayer[] players) => Active?.SetPlayers(players) ?? false;

    public bool EditSlot(int room, SlotType type) => Active?.EditSlot(room, type) ?? false;

    public SlotType GetSlotType(int room) => Active?.GetSlotType(room) ?? Roster.Slots[room].Type;

    public bool IsSlotEditPending(int room) => Active?.IsSlotEditPending(room) ?? false;

    public bool ApplySlotType(SlotType type) => Active?.ApplySlotType(type) ?? false;

    public bool RemovePlayer(int peer, int id) => Active?.RemovePlayer(peer, id) ?? false;

    public bool SetSpectating(int peer, bool spectating) => Active?.SetSpectating(peer, spectating) ?? false;

    public void Kick(int peer, bool ban) => Active?.Kick(peer, ban);

    public void SetMatchSettings(string settings) => Active?.SetMatchSettings(settings);

    public bool StartMatch(string settings) => Active?.StartMatch(settings) ?? false;

    public bool ReturnToLobby() => Active?.ReturnToLobby() ?? false;

    public ulong SessionId => Active?.SessionId ?? 0;
    public IReadOnlyList<int> PeerIds => Active?.PeerIds ?? [];
    public IRollbackSession? LobbySession => Active?.LobbySession;
    public bool SimulationReady => Active?.SimulationReady ?? false;
    public bool Transitioning => Active?.Transitioning ?? true;
    private LobbySimulation? pendingSimulation;

    private RollbackPreferences rollback = new();
    public RollbackPreferences RollbackSettings => rollback;
    public IReadOnlyDictionary<int, RollbackPreferences> PeerRollbackSettings =>
        Active?.PeerRollbackSettings ?? new Dictionary<int, RollbackPreferences>();

    public void SetRollbackSettings(RollbackPreferences preferences)
    {
        if (!preferences.IsValid)
            throw new ArgumentException("Invalid rollback preferences", nameof(preferences));
        rollback = preferences;
        Active?.SetRollbackSettings(preferences);
    }

    public void AttachSimulation(LobbySimulation simulation)
    {
        pendingSimulation = simulation;
        Active?.AttachSimulation(simulation);
    }

    protected void AttachPendingSimulation()
    {
        Active?.SetRollbackSettings(rollback);
        if (pendingSimulation != null && Active != null)
            Active.AttachSimulation(pendingSimulation);
    }

    public abstract void Poll();
    public abstract IPeerTransport CreateTransport();
    public abstract void Dispose();
}
