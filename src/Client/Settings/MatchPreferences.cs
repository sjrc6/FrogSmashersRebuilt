using FrogSmashers.Core;

namespace FrogSmashers.Client;

public sealed record MatchPreferences
{
    public MatchFormat Format { get; set; }
    public ScoringMode Scoring { get; set; }
    public int WinScore { get; set; }
    public int StartingStocks { get; set; } = 5;
    public int MatchRounds { get; set; } = 6;
    public int FirstMap { get; set; }
    public bool ShuffleMaps { get; set; }

    public void ChangeFormat(int amount)
    {
        Format = (MatchFormat)(((int)Format + amount + 3) % 3);
        if (Format == MatchFormat.Crews)
            Scoring = ScoringMode.Stocks;
    }
}
