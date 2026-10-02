namespace FrogSmashers.Network;

public readonly record struct LobbyCpuCommand(int Revision, byte Rooms, byte Enabled, uint Colors, uint Teams)
{
    public bool IsValid =>
        Revision >= 0
        && (Enabled & ~Rooms) == 0
        && Colors < 1u << 24
        && Teams < 1u << 24
        && (Revision == 0 ? this == default : Rooms != 0);

    public int Color(int room) => (int)(Colors >> (room * 3)) & 7;

    public int Team(int room) => (int)(Teams >> (room * 3)) & 7;

    internal static LobbyCpuCommand FromRoster(int revision, byte rooms, LobbyRoster roster)
    {
        byte enabled = 0;
        uint colors = 0,
            teams = 0;
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
        {
            if ((rooms & (1 << room)) == 0 || roster.Slots[room].Player is not { Cpu: true } player)
                continue;
            enabled |= (byte)(1 << room);
            colors |= (uint)player.Color << (room * 3);
            teams |= (uint)player.Team << (room * 3);
        }
        return new(revision, rooms, enabled, colors, teams);
    }
}

public readonly record struct LobbyInputSource(int Peer, int Id, int Room)
{
    public bool HostCommand => Room == -1;
}
