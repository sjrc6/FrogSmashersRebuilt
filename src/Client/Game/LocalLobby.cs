using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed class LocalLobby
{
    public LobbyRoster Roster { get; } = new();
    public IReadOnlyList<LocalSeat> Seats =>
        Roster
            .Players(0)
            .Where(player => player.Spawned)
            .Select(player => new LocalSeat(player.Cpu ? -1 : player.Id, player.Team, player.Color, player.Id))
            .ToArray();

    public bool Join(int device, int? team = null, bool spawned = true)
    {
        var players = Roster.Players(0);
        if (device < -1 || device >= 10 || device >= 0 && players.Any(player => player.Id == device))
            return false;
        int room = Enumerable
            .Range(0, 8)
            .FirstOrDefault(
                index =>
                    Roster.Slots[index].Player == null
                    && Roster.Slots[index].Open
                    && Roster.Slots[index].Type != SlotType.Cpu,
                -1
            );
        if (room < 0)
            return false;
        var player = new LobbyPlayer(
            device < 0 ? Enumerable.Range(10, 8).First(id => players.All(player => player.Id != id)) : device,
            Team: team is >= 0 ? team.Value : players.Length % 2,
            Color: room,
            Spawned: spawned,
            Cpu: device < 0
        );
        return Roster.SetPlayers(0, [.. players, player]);
    }

    public void Remove(int index)
    {
        var players = Roster.Players(0);
        if (index >= 0 && index < players.Length)
            Roster.SetPlayers(0, players.Where((_, i) => i != index).ToArray());
    }

    public void SetTeam(int index, int team)
    {
        var players = Roster.Players(0);
        if (index >= 0 && index < players.Length && team is >= 0 and < 8)
        {
            players[index] = players[index] with { Team = team };
            Roster.SetPlayers(0, players);
        }
    }
}
