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

    private MatchPreferences DefaultPreferences() =>
        new()
        {
            FirstMap = defaultFirstMap,
            Modifiers = GameModifiers.Default with { IncludePodium = maps[defaultFirstMap].Role == MapRole.ExtraArena },
        };

    public void ResetPreferences() => Preferences = DefaultPreferences();

    public void ChangeFirstMap(int amount)
    {
        var available = ArenaRotation.Available(maps, Preferences.Modifiers);
        int current = Array.IndexOf(available, FirstMap);
        FirstMap = available[(Math.Max(0, current) + amount + available.Length) % available.Length];
    }

    public void SetModifiers(GameModifiers modifiers)
    {
        modifiers.Validate();
        int previousArenaCount = ArenaRotation.Available(maps, Preferences.Modifiers).Length;
        if (Preferences.MatchRounds == previousArenaCount)
            Preferences.MatchRounds = ArenaRotation.Available(maps, modifiers).Length;
        Preferences.Modifiers = modifiers;
        if (maps[FirstMap].Role == MapRole.ExtraArena && !modifiers.IncludePodium)
            FirstMap = ArenaRotation.Available(maps, modifiers)[0];
    }

    public static LobbyPlayer[] MatchPlayers(LobbyRoster roster) =>
        roster
            .Slots.Where(slot => slot.Player != null)
            .Select(slot => slot.Player!)
            .OrderBy(player => player.Peer)
            .ToArray();

    public MatchOptions CreateOptions(int[]? customMapOrder = null, LobbyRoster? roster = null)
    {
        var players = MatchPlayers(roster ?? Lobby.Roster);
        var order = customMapOrder?.ToArray() ?? ArenaRotation.StartingAt(maps, Preferences.Modifiers, FirstMap);
        if (customMapOrder == null && Preferences.ShuffleMaps)
            new Random((int)Seed).Shuffle(order.AsSpan(1));

        var rules = new GameRules(
            playerCount: players.Length,
            cpuPlayers: [.. Enumerable.Range(0, 8).Select(index => index < players.Length && players[index].Cpu)],
            teams:
            [
                .. Enumerable.Range(0, 8).Select(index => index < players.Length ? players[index].Team : index % 2),
            ],
            colors: [.. Enumerable.Range(0, 8).Select(index => index < players.Length ? players[index].Color : index)],
            format: Preferences.Format,
            scoring: Preferences.Scoring,
            winScore: Preferences.WinScore,
            matchRounds: Preferences.MatchRounds,
            startingStocks: Preferences.StartingStocks,
            mapOrder: [.. order],
            modifiers: Preferences.Modifiers
        );
        return new MatchOptions(rules, Seed, Preferences.ShuffleMaps);
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
