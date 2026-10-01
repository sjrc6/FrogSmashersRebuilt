namespace FrogSmashers.Core;

public enum HitKind
{
    Bat,
    Tongue,
}

public enum SimulationEventKind
{
    Spawn,
    Jump,
    Land,
    Turn,
    Charge,
    Swing,
    Hit,
    TongueLaunch,
    TongueHit,
    TongueLatch,
    Burp,
    Bounce,
    Dodge,
    Death,
    RoundWin,
    MatchWin,
    FlySpawn,
    Footstep,
    Launch,
    LobbyPreview,
}

public readonly record struct SimulationEvent(
    long Tick,
    int Sequence,
    SimulationEventKind Kind,
    int Player,
    int Other,
    Fixed X,
    Fixed Y,
    Fixed Strength,
    Fixed Power = default,
    int ComboHits = 0,
    HitKind HitKind = HitKind.Bat,
    Fixed VelocityX = default,
    Fixed VelocityY = default,
    int SurfaceSide = 0,
    Fixed HitstopSeconds = default,
    Fixed HitEffectX = default,
    Fixed HitEffectY = default,
    bool AwardedScore = false
)
{
    public long Id => (Tick << 12) | (uint)Sequence;
}
