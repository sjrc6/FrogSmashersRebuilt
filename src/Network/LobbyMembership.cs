namespace FrogSmashers.Network;

public sealed class LobbyMembership
{
    public IReadOnlyList<LobbyPlayer?> Rooms { get; }
    public IReadOnlyList<LobbyPlayer> Spectators { get; }
    public int Count => Rooms.Count(player => player != null);

    public LobbyMembership(IEnumerable<LobbyPlayer?> rooms, IEnumerable<LobbyPlayer> spectators)
    {
        var occupants = rooms.ToArray();
        var observers = spectators.ToArray();
        var players = occupants.OfType<LobbyPlayer>().ToArray();
        if (
            occupants.Length != LobbyRoster.MaxPlayers
            || observers.Length > LobbyRoster.MaxSpectators
            || players.Any(player => !LobbyRoster.ValidPlayer(player))
            || observers.Any(player => player == null || !LobbyRoster.ValidPlayer(player) || player.Cpu)
            || players.Select(player => (player.Peer, player.Id)).Distinct().Count() != players.Length
            || players.Select(player => player.Color).Distinct().Count() != players.Length
            || observers.Select(player => player.Peer).Distinct().Count() != observers.Length
            || observers.Any(observer => players.Any(player => !player.Cpu && player.Peer == observer.Peer))
        )
            throw new ArgumentException("Invalid lobby membership");
        Rooms = Array.AsReadOnly(occupants);
        Spectators = Array.AsReadOnly(observers);
    }

    public LobbyPlayer[] Players(int peer) =>
        Rooms.OfType<LobbyPlayer>().Where(player => player.Peer == peer).ToArray();

    public LobbyPlayer[] Humans(int peer) => Players(peer).Where(player => !player.Cpu).ToArray();

    public LobbyPlayer? Spectator(int peer) => Spectators.FirstOrDefault(player => player.Peer == peer);
}
