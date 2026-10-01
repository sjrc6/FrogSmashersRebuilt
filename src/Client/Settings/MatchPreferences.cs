using FrogSmashers.Core;

namespace FrogSmashers.Client;

public sealed record MatchPreferences
{
    public bool TeamMode { get; set; }
    public int WinScore { get; set; }
    public int MatchRounds { get; set; } = 6;
    public int FirstMap { get; set; }
    public bool ShuffleMaps { get; set; }

    internal GameRules CreateRules() =>
        new()
        {
            TeamMode = TeamMode,
            WinScore = WinScore,
            MatchRounds = MatchRounds,
        };
}
