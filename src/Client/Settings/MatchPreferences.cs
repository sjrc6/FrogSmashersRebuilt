using FrogSmashers.Core;

namespace FrogSmashers.Client;

// Saved choices for the next hosted match. Player assignments and received rules never belong here.
public sealed record MatchPreferences
{
    public bool TeamMode { get; set; }
    public int WinScore { get; set; }
    public int MatchRounds { get; set; } = 6;
    public bool CharactersBounceEachOther { get; set; }
    public int FirstMap { get; set; }
    public bool ShuffleMaps { get; set; }

    internal void Normalize()
    {
        WinScore = Math.Clamp(WinScore, 0, 30);
        MatchRounds = Math.Clamp(MatchRounds, 1, 20);
        FirstMap = Math.Clamp(FirstMap, 0, 6);
    }

    internal GameRules CreateRules() =>
        new()
        {
            TeamMode = TeamMode,
            WinScore = WinScore,
            MatchRounds = MatchRounds,
            CharactersBounceEachOther = CharactersBounceEachOther,
        };
}
