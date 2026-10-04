using System.Text.Json.Serialization;

namespace FrogSmashers.Core;

public enum MatchScoring
{
    RoundWins,
    CumulativePoints,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GameModifiers
{
    public const int MinimumFlyDelay = 1;
    public const int MaximumFlyDelay = 120;
    public static GameModifiers Default { get; } = new();

    public bool PhysicsFixes { get; init; } = true;
    public bool BodyBouncing { get; init; }
    public bool BounceBeforeRecoveryOnly { get; init; } = true;
    public bool RedirectBounces { get; init; }
    public bool SuicidePenalty { get; init; }
    public MatchScoring MatchScoring { get; init; }
    public bool FlyEnabled { get; init; } = true;
    public int FlySpawnMinSeconds { get; init; } = 15;
    public int FlySpawnMaxSeconds { get; init; } = 45;
    public bool IncludePodium { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(MatchScoring))
            throw new ArgumentException("Invalid match scoring modifier");
        if (
            FlySpawnMinSeconds < MinimumFlyDelay
            || FlySpawnMaxSeconds > MaximumFlyDelay
            || FlySpawnMinSeconds > FlySpawnMaxSeconds
        )
            throw new ArgumentException("Fly delays must be ordered and between 1 and 120 seconds");
    }

    internal void WriteConfiguration(BinaryWriter writer)
    {
        writer.Write(PhysicsFixes);
        writer.Write(BodyBouncing);
        writer.Write(BounceBeforeRecoveryOnly);
        writer.Write(RedirectBounces);
        writer.Write(SuicidePenalty);
        writer.Write((int)MatchScoring);
        writer.Write(FlyEnabled);
        writer.Write(FlySpawnMinSeconds);
        writer.Write(FlySpawnMaxSeconds);
        writer.Write(IncludePodium);
    }
}
