using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed class LobbyCreation
{
    private int type;
    private int capacity = 8;
    public bool Lan => type == 3;
    public string TypeLabel => new[] { "PRIVATE", "FRIENDS", "PUBLIC", "LAN / UDP" }[type];
    public SlotType SlotType =>
        type switch
        {
            0 => SlotType.Private,
            1 => SlotType.Friend,
            _ => SlotType.Open,
        };

    public int Capacity(LobbyRoster roster) => Math.Clamp(capacity, Math.Max(1, roster.Count), 8);

    public void Reset(LobbyRoster roster) => capacity = Math.Max(1, roster.Capacity);

    public void CycleType(int direction) => type = (type + direction + 4) % 4;

    public void ChangeCapacity(int direction, LobbyRoster roster) =>
        capacity = Math.Clamp(Capacity(roster) + direction, Math.Max(1, roster.Count), 8);

    public void Apply(LobbyRoster roster) => roster.ConfigureEmpty(SlotType, Capacity(roster));
}
