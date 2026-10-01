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
    public int[][] PeerSlots => Active?.PeerSlots ?? [];
    public int[] PlayerTeams => Active?.PlayerTeams ?? [];
    public int[] PlayerColors => Active?.PlayerColors ?? [];
    public string MatchSettingsJson => Active?.MatchSettingsJson ?? "";

    public bool SetPlayers(LobbyPlayer[] players) => Active?.SetPlayers(players) ?? false;

    public bool EditSlot(int room, SlotType type, bool open, bool remove = false) =>
        Active?.EditSlot(room, type, open, remove) ?? false;

    public void Kick(int peer, bool ban) => Active?.Kick(peer, ban);

    public void SetMatchSettings(string settings) => Active?.SetMatchSettings(settings);

    public bool StartMatch(string settings) => Active?.StartMatch(settings) ?? false;

    public bool ReturnToLobby() => Active?.ReturnToLobby() ?? false;

    public void SendLobbyInputs(InputFrame[] inputs) => Active?.SendLobbyInputs(inputs);

    public InputFrame[] ReadLobbyInputs() => Active?.ReadLobbyInputs() ?? new InputFrame[8];

    public void SendSnapshot(byte[] snapshot) => Active?.SendSnapshot(snapshot);

    public byte[]? TakeSnapshot() => Active?.TakeSnapshot();

    public abstract void Poll();
    public abstract IPeerTransport CreateTransport();
    public abstract void Dispose();
}
