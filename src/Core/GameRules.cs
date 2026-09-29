namespace FrogSmashers.Core;

public enum MatchPhase
{
    Playing,
    RoundFinished,
    MatchFinished,
    RoundScores,
}

public sealed class GameRules
{
    public int PlayerCount { get; set; } = 2;
    public bool TeamMode { get; set; }
    public int[] Teams { get; set; } = [0, 1, 0, 1, 0, 1, 0, 1];
    public int WinScore { get; set; }
    public int MatchRounds { get; set; } = 6;
    public bool CharactersBounceEachOther { get; set; }
    public bool WeirdBounceTrajectories { get; set; }
    public bool OnlyBounceBeforeRecover { get; set; } = true;
    public bool PreservePlatformEmbedding { get; set; } = true;
    public bool Showdown { get; set; }
    public int RoundFinishTicks { get; set; } = 900;
    public int ScoreScreenTicks { get; set; } = 720;
    public int[] MapOrder { get; set; } = [];
}
