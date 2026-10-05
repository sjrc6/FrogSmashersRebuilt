using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed class LobbyCreation
{
    private int type = 2;
    private int capacity = 8;
    public bool Lan => type == 3;
    public LobbyPrivacy Privacy =>
        type switch
        {
            0 => LobbyPrivacy.Private,
            1 => LobbyPrivacy.Friends,
            4 => LobbyPrivacy.PrivateCode,
            _ => LobbyPrivacy.Public,
        };
    public string TypeLabel => new[] { "PRIVATE", "FRIENDS", "PUBLIC", "LAN / UDP", "PRIVATE CODE" }[type];
    public SlotType SlotType =>
        type switch
        {
            0 or 4 => SlotType.Private,
            1 => SlotType.Friend,
            _ => SlotType.Open,
        };

    public int Capacity(LobbyRoster roster) => Math.Clamp(capacity, Math.Max(1, roster.Count), 8);

    public void Reset(LobbyRoster roster) => capacity = Math.Max(1, roster.Capacity);

    public void CycleType(int direction) => type = (type + direction + 5) % 5;

    public void SelectPrivate() => type = 0;

    public void ChangeCapacity(int direction, LobbyRoster roster) =>
        capacity = Math.Clamp(Capacity(roster) + direction, Math.Max(1, roster.Count), 8);

    public void Apply(LobbyRoster roster) => roster.ConfigureEmpty(SlotType, Capacity(roster));
}
