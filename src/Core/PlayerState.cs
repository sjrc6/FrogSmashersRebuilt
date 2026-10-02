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
    public int ColorIndex;
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

    internal void WriteSnapshot(BinaryWriter writer)
    {
        writer.Write(Slot);
        writer.Write(Team);
        writer.Write(ColorIndex);
        writer.Write(Facing);
        writer.Write(LastHitBy);
        writer.Write(HitsTaken);
        writer.Write(Score);
        writer.Write(RoundWins);
        writer.Write(WallSlideSide);
        writer.Write(SpawnTicks);
        writer.Write(HitstopTicks);
        writer.Write(Alive);
        writer.Write(Eliminated);
        writer.Write(OnGround);
        writer.Write(WallSliding);
        writer.Write(HasFly);
        writer.Write(WasHitDownwards);
        writer.Write(HasReachedApex);
        writer.Write(CanBounceDodge);
        writer.Write(HasBounceDodged);
        writer.Write(CanBounceTongue);
        writer.Write(HasBounceTongued);
        writer.Write(WasBouncingBeforeTongue);
        writer.Write(X.Raw);
        writer.Write(Y.Raw);
        writer.Write(VX.Raw);
        writer.Write(VY.Raw);
        writer.Write(AttackX.Raw);
        writer.Write(AttackY.Raw);
        writer.Write(TongueX.Raw);
        writer.Write(TongueY.Raw);
        writer.Write(TongueDistance.Raw);
        writer.Write(AttackCharge.Raw);
        writer.Write(AttackTimeLeft.Raw);
        writer.Write(AttackRecoverTimeLeft.Raw);
        writer.Write(TongueDelayLeft.Raw);
        writer.Write(JumpGraceLeft.Raw);
        writer.Write(GravityGraceLeft.Raw);
        writer.Write(JumpCooldownLeft.Raw);
        writer.Write(TimeSinceHit.Raw);
        writer.Write(BounceGravityRestore.Raw);
        writer.Write(SkidRecoverLeft.Raw);
        writer.Write(HitstopScale.Raw);
        writer.Write(LocalDelta.Raw);
        writer.Write(AnimationTime.Raw);
        writer.Write((int)Mode);
        writer.Write((int)AttackPhase);
        writer.Write((int)TonguePhase);
        writer.Write(PreviousInput.Packed);
        writer.Write(AnimationKey);
        writer.Write(StateStartTick);
    }

    internal static PlayerState ReadSnapshot(BinaryReader reader) =>
        new()
        {
            Slot = reader.ReadInt32(),
            Team = reader.ReadInt32(),
            ColorIndex = reader.ReadInt32(),
            Facing = reader.ReadInt32(),
            LastHitBy = reader.ReadInt32(),
            HitsTaken = reader.ReadInt32(),
            Score = reader.ReadInt32(),
            RoundWins = reader.ReadInt32(),
            WallSlideSide = reader.ReadInt32(),
            SpawnTicks = reader.ReadInt32(),
            HitstopTicks = reader.ReadInt32(),
            Alive = reader.ReadBoolean(),
            Eliminated = reader.ReadBoolean(),
            OnGround = reader.ReadBoolean(),
            WallSliding = reader.ReadBoolean(),
            HasFly = reader.ReadBoolean(),
            WasHitDownwards = reader.ReadBoolean(),
            HasReachedApex = reader.ReadBoolean(),
            CanBounceDodge = reader.ReadBoolean(),
            HasBounceDodged = reader.ReadBoolean(),
            CanBounceTongue = reader.ReadBoolean(),
            HasBounceTongued = reader.ReadBoolean(),
            WasBouncingBeforeTongue = reader.ReadBoolean(),
            X = new(reader.ReadInt64()),
            Y = new(reader.ReadInt64()),
            VX = new(reader.ReadInt64()),
            VY = new(reader.ReadInt64()),
            AttackX = new(reader.ReadInt64()),
            AttackY = new(reader.ReadInt64()),
            TongueX = new(reader.ReadInt64()),
            TongueY = new(reader.ReadInt64()),
            TongueDistance = new(reader.ReadInt64()),
            AttackCharge = new(reader.ReadInt64()),
            AttackTimeLeft = new(reader.ReadInt64()),
            AttackRecoverTimeLeft = new(reader.ReadInt64()),
            TongueDelayLeft = new(reader.ReadInt64()),
            JumpGraceLeft = new(reader.ReadInt64()),
            GravityGraceLeft = new(reader.ReadInt64()),
            JumpCooldownLeft = new(reader.ReadInt64()),
            TimeSinceHit = new(reader.ReadInt64()),
            BounceGravityRestore = new(reader.ReadInt64()),
            SkidRecoverLeft = new(reader.ReadInt64()),
            HitstopScale = new(reader.ReadInt64()),
            LocalDelta = new(reader.ReadInt64()),
            AnimationTime = new(reader.ReadInt64()),
            Mode = (CharacterMode)reader.ReadInt32(),
            AttackPhase = (AttackPhase)reader.ReadInt32(),
            TonguePhase = (TonguePhase)reader.ReadInt32(),
            PreviousInput = InputFrame.FromPacked(reader.ReadUInt32()),
            AnimationKey = reader.ReadString(),
            StateStartTick = reader.ReadInt64(),
        };
}
