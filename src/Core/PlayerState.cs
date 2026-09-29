namespace FrogSmashers.Core;

public enum CharacterMode
{
    Normal,
    Attacking,
    Bouncing,
    Tongue,
}

public enum AttackPhase
{
    Idle,
    Charging,
    Swing,
    Recovering,
}

public enum TonguePhase
{
    Extending,
    Retracting,
    AttachedToTerrain,
    RetractingHitEnemy,
    RetractingHitEnemyTongue,
    RetractingHitFly,
    Stunned,
    Burping,
}

public sealed class PlayerState
{
    public int Slot;
    public int Team;
    public int Facing = 1;
    public int LastHitBy = -1;
    public int HitsTaken;
    public int Score;
    public int RoundWins;
    public bool Alive;
    public bool Eliminated;
    public bool OnGround;
    public bool WallSliding;
    public bool HasFly;
    public bool WasHitDownwards;
    public bool HasReachedApex;
    public bool CanBounceDodge;
    public bool HasBounceDodged;
    public bool CanBounceTongue;
    public bool HasBounceTongued;
    public bool WasBouncingBeforeTongue;
    public int WallSlideSide;
    public int SpawnTicks;
    public int HitstopTicks;
    public Fixed X;
    public Fixed Y;
    public Fixed VX;
    public Fixed VY;
    public Fixed AttackX = 1;
    public Fixed AttackY;
    public Fixed TongueX = 1;
    public Fixed TongueY;
    public Fixed TongueDistance;
    public Fixed AttackCharge;
    public Fixed AttackTimeLeft;
    public Fixed AttackRecoverTimeLeft;
    public Fixed TongueDelayLeft;
    public Fixed JumpGraceLeft;
    public Fixed GravityGraceLeft;
    public Fixed JumpCooldownLeft;
    public Fixed TimeSinceHit;
    public Fixed BounceGravityRestore;
    public Fixed SkidRecoverLeft;
    public Fixed HitstopScale = 1;
    public Fixed LocalDelta;
    public Fixed AnimationTime;
    public CharacterMode Mode;
    public AttackPhase AttackPhase;
    public TonguePhase TonguePhase;
    public InputFrame PreviousInput;
    public string AnimationKey = "idle";
    public long StateStartTick;
    public Fixed ChargeRatio => Fixed.Clamp(AttackCharge / Fixed.FromDecimal(1.25m), 0, 1);
    public FixedVector Position => new(X, Y);
    public FixedVector Center => new(X, Y + 1);
    public FixedVector Velocity => new(VX, VY);
    public FixedVector TongueOrigin => new(X, Y + Fixed.FromDecimal(1.5m));
    public FixedVector TongueTip => TongueOrigin + new FixedVector(TongueX, TongueY) * TongueDistance;
}
