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
    public bool[] CpuPlayers { get; set; } = new bool[8];
    public int[] Colors { get; set; } = [0, 1, 2, 3, 4, 5, 6, 7];
    public bool TeamMode { get; set; }
    public int[] Teams { get; set; } = [0, 1, 0, 1, 0, 1, 0, 1];
    public int WinScore { get; set; }
    public int MatchRounds { get; set; } = 6;
    public bool Showdown { get; set; }
    public int RoundFinishTicks { get; set; } = World.TickRate * 15 / 2;
    public int ScoreScreenTicks { get; set; } = World.TickRate * 6;
    public int[] MapOrder { get; set; } = [];
}
