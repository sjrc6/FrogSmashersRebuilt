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
    public bool TongueHurtbox { get; init; }
    public bool TongueJump { get; init; }
    public bool ChargeTongues { get; init; }
    public bool SuicidePenalty { get; init; }
    public MatchScoring MatchScoring { get; init; }
    public bool FlyEnabled { get; init; } = true;
    public bool FlyTumble { get; init; }
    public bool FlyExplode { get; init; }
    public int FlySpawnMinSeconds { get; init; } = 15;
    public int FlySpawnMaxSeconds { get; init; } = 45;

    public void Validate()
    {
        if (!Enum.IsDefined(MatchScoring))
            throw new ArgumentException("Invalid match scoring modifier");
        if (FlyTumble && FlyExplode)
            throw new ArgumentException("Choose one fly hit modifier");
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
        writer.Write(TongueHurtbox);
        writer.Write(TongueJump);
        writer.Write(ChargeTongues);
        writer.Write(SuicidePenalty);
        writer.Write((int)MatchScoring);
        writer.Write(FlyEnabled);
        writer.Write(FlyTumble);
        writer.Write(FlyExplode);
        writer.Write(FlySpawnMinSeconds);
        writer.Write(FlySpawnMaxSeconds);
    }
}
