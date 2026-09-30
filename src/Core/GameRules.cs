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
    public bool Lobby { get; set; }
    public int[] Colors { get; set; } = [0, 1, 2, 3, 4, 5, 6, 7];
    public bool TeamMode { get; set; }
    public int[] Teams { get; set; } = [0, 1, 0, 1, 0, 1, 0, 1];
    public int WinScore { get; set; }
    public int MatchRounds { get; set; } = 6;
    public bool CharactersBounceEachOther { get; set; }
    public bool Showdown { get; set; }
    public int RoundFinishTicks { get; set; } = 900;
    public int ScoreScreenTicks { get; set; } = 720;
    public int[] MapOrder { get; set; } = [];
}
