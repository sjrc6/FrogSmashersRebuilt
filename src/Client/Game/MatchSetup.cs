using FrogSmashers.Core;
using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed class MatchSetup
{
    private readonly int defaultFirstMap;
    private readonly IReadOnlyList<MapData> maps;
    public MatchPreferences Preferences { get; private set; }
    public LocalLobby Lobby { get; } = new();
    public IReadOnlyList<LocalSeat> Seats => Lobby.Seats;
    public uint Seed { get; }
    public int FirstMap
    {
        get => Preferences.FirstMap;
        set => Preferences.FirstMap = value;
    }

    public MatchSetup(uint seed, IReadOnlyList<MapData> maps, int firstMap = 0)
    {
        Seed = seed;
        this.maps = maps;
        defaultFirstMap = firstMap;
        Preferences = DefaultPreferences();
    }

    private MatchPreferences DefaultPreferences() => new() { FirstMap = defaultFirstMap };

    public void ResetPreferences() => Preferences = DefaultPreferences();

    public void CommitPreferences(MatchPreferences preferences) => Preferences = preferences;

    public void ChangeFirstMap(MatchPreferences preferences, int amount)
    {
        var available = ArenaRotation.Available(maps);
        int current = Array.IndexOf(available, preferences.FirstMap);
        preferences.FirstMap = available[(Math.Max(0, current) + amount + available.Length) % available.Length];
    }

    public void SetModifiers(GameModifiers modifiers)
    {
        modifiers.Validate();
        Preferences.Modifiers = modifiers;
    }

    public static LobbyPlayer[] MatchPlayers(LobbyRoster roster) =>
        roster
            .Slots.Where(slot => slot.Player != null)
            .Select(slot => slot.Player!)
            .OrderBy(player => player.Peer)
            .ToArray();

    public MatchOptions CreateOptions(
        int[]? customMapOrder = null,
        LobbyRoster? roster = null,
        MatchPreferences? preferences = null
    )
    {
        preferences ??= Preferences;
        var players = MatchPlayers(roster ?? Lobby.Roster);
        var order = customMapOrder?.ToArray() ?? ArenaRotation.StartingAt(maps, preferences.FirstMap);
        if (customMapOrder == null && preferences.ShuffleMaps)
            new Random((int)Seed).Shuffle(order.AsSpan(1));

        var rules = new GameRules(
            playerCount: players.Length,
            cpuPlayers: [.. Enumerable.Range(0, 8).Select(index => index < players.Length && players[index].Cpu)],
            teams:
            [
                .. Enumerable.Range(0, 8).Select(index => index < players.Length ? players[index].Team : index % 2),
            ],
            colors: [.. Enumerable.Range(0, 8).Select(index => index < players.Length ? players[index].Color : index)],
            format: preferences.Format,
            scoring: preferences.Scoring,
            winScore: preferences.WinScore,
            matchRounds: preferences.MatchRounds,
            startingStocks: preferences.StartingStocks,
            mapOrder: [.. order],
            modifiers: preferences.Modifiers
        );
        return new MatchOptions(rules, Seed, preferences.ShuffleMaps);
    }

    public static void ValidateRoster(GameRules rules, LobbyRoster roster)
    {
        var players = MatchPlayers(roster);
        if (rules.PlayerCount != players.Length)
            throw new InvalidDataException("Match configuration does not match the lobby roster");
        for (int slot = 0; slot < players.Length; slot++)
        {
            var player = players[slot];
            if (
                player.Cpu != rules.CpuPlayers[slot]
                || player.Team != rules.Teams[slot]
                || player.Color != rules.Colors[slot]
            )
                throw new InvalidDataException("Match configuration does not match the lobby roster");
        }
    }
}
