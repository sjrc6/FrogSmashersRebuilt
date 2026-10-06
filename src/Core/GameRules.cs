using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace FrogSmashers.Core;

public enum MatchFormat
{
    Ffa,
    Teams,
    Crews,
}

public enum ScoringMode
{
    Points,
    Stocks,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GameRules
{
    public int PlayerCount { get; }
    public bool Lobby { get; }
    public ImmutableArray<bool> CpuPlayers { get; }
    public ImmutableArray<int> Colors { get; }
    public MatchFormat Format { get; }
    public ScoringMode Scoring { get; }
    public ImmutableArray<int> Teams { get; }
    public int WinScore { get; }
    public int MatchRounds { get; }
    public int StartingStocks { get; }
    public bool Showdown { get; }
    public int RoundFinishTicks { get; }
    public int ScoreScreenTicks { get; }
    public ImmutableArray<int> MapOrder { get; }
    public GameModifiers Modifiers { get; }

    [JsonIgnore]
    public bool UsesTeams => Format != MatchFormat.Ffa;

    [JsonIgnore]
    public bool BodyBouncingEnabled => !Lobby && Modifiers.BodyBouncing;

    [JsonIgnore]
    public bool CumulativeScoring =>
        Scoring == ScoringMode.Points && Modifiers.MatchScoring == MatchScoring.CumulativePoints;

    [JsonIgnore]
    public int TargetScore =>
        WinScore > 0 ? WinScore
        : !UsesTeams && PlayerCount == 2 ? 5
        : 10;

    public GameRules(
        int playerCount = 2,
        bool lobby = false,
        ImmutableArray<bool> cpuPlayers = default,
        ImmutableArray<int> colors = default,
        MatchFormat format = MatchFormat.Ffa,
        ScoringMode scoring = ScoringMode.Points,
        ImmutableArray<int> teams = default,
        int winScore = 0,
        int matchRounds = 6,
        int startingStocks = 5,
        bool showdown = false,
        int roundFinishTicks = World.TickRate * 15 / 2,
        int scoreScreenTicks = World.TickRate * 6,
        ImmutableArray<int> mapOrder = default,
        GameModifiers? modifiers = null
    )
    {
        if (playerCount is < 0 or > 8 || !Enum.IsDefined(format) || !Enum.IsDefined(scoring))
            throw new ArgumentException("Invalid player count or match mode");
        if (winScore < 0 || matchRounds < 1 || startingStocks < 1 || roundFinishTicks < 0 || scoreScreenTicks < 0)
            throw new ArgumentException("Invalid match limits");
        if (format == MatchFormat.Crews && scoring != ScoringMode.Stocks)
            throw new ArgumentException("Crews requires Stocks");

        CpuPlayers = cpuPlayers.IsDefault ? [false, false, false, false, false, false, false, false] : cpuPlayers;
        Colors = colors.IsDefault ? [0, 1, 2, 3, 4, 5, 6, 7] : colors;
        Teams = teams.IsDefault ? [0, 1, 0, 1, 0, 1, 0, 1] : teams;
        MapOrder = mapOrder.IsDefault ? [] : mapOrder;
        if (CpuPlayers.Length != 8 || Colors.Length != 8 || Colors.Any(color => color is < 0 or > 7))
            throw new ArgumentException("Invalid player colors or CPU assignments");
        if (Teams.Length < playerCount || Teams.Any(team => team is < 0 or > 7) || MapOrder.Any(map => map < 0))
            throw new ArgumentException("Invalid teams or map order");

        PlayerCount = playerCount;
        Lobby = lobby;
        Format = format;
        Scoring = scoring;
        WinScore = winScore;
        MatchRounds = matchRounds;
        StartingStocks = startingStocks;
        Showdown = showdown;
        RoundFinishTicks = roundFinishTicks;
        ScoreScreenTicks = scoreScreenTicks;
        Modifiers = modifiers ?? GameModifiers.Default;
        Modifiers.Validate();
    }

    public MatchStartBlock? StartBlockedReason()
    {
        if (PlayerCount < 2)
            return MatchStartBlock.TooFewPlayers;
        int teamCount = Teams.Take(PlayerCount).Distinct().Count();
        if (Format == MatchFormat.Teams && teamCount < 2)
            return MatchStartBlock.TooFewTeams;
        if (Format == MatchFormat.Crews && CpuPlayers.Take(PlayerCount).Any(cpu => cpu))
            return MatchStartBlock.CpuInCrews;
        return null;
    }

    internal void ValidateWorld(IReadOnlyList<MapData> maps)
    {
        if (StartBlockedReason() is { } reason)
            throw new ArgumentException($"Cannot start match: {reason}");
        if (maps.Count == 0 || MapOrder.Any(map => map >= maps.Count))
            throw new ArgumentException("Invalid map order");
        if (Lobby && maps.Any(map => map.Spawns.Count < PlayerCount))
            throw new ArgumentException("A lobby needs a spawn point for every room");
    }

    internal void WriteConfiguration(BinaryWriter writer)
    {
        writer.Write(PlayerCount);
        writer.Write(Lobby);
        foreach (bool cpu in CpuPlayers)
            writer.Write(cpu);
        foreach (int color in Colors)
            writer.Write(color);
        writer.Write((int)Format);
        writer.Write((int)Scoring);
        for (int slot = 0; slot < PlayerCount; slot++)
            writer.Write(Teams[slot]);
        writer.Write(WinScore);
        writer.Write(MatchRounds);
        writer.Write(StartingStocks);
        writer.Write(Showdown);
        writer.Write(RoundFinishTicks);
        writer.Write(ScoreScreenTicks);
        writer.Write(MapOrder.Length);
        foreach (int map in MapOrder)
            writer.Write(map);
        Modifiers.WriteConfiguration(writer);
    }
}
