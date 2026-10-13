namespace FrogSmashers.Core;

public enum FlyPhase
{
    Flying,
    Tumbling,
    Recovering,
    Exploding,
}

public sealed class FlyState
{
    public static readonly Fixed Radius = Fixed.FromDecimal(.75m);
    public bool Active;
    public FlyPhase Phase;
    public bool HasBounced;
    public int HitsTaken;
    public int LastHitBy = -1;
    public int HitstopTicks;
    public int RecoveryTicks;
    public Fixed TimeSinceHit;
    public Fixed GravityRestore;
    public Fixed ExplosionPower;
    public int Owner = -1;
    public int IngestedBy = -1;
    public int ClaimTicks;
    public int SpawnTicks;
    public int DirectionTicks;
    public Fixed X;
    public Fixed Y;
    public Fixed VX;
    public Fixed VY;
    public Fixed TargetVX;
    public Fixed TargetVY;
    public FixedVector Position => new(X, Y);

    internal void WriteSnapshot(BinaryWriter writer)
    {
        writer.Write(Active);
        writer.Write((int)Phase);
        writer.Write(HasBounced);
        writer.Write(HitsTaken);
        writer.Write(LastHitBy);
        writer.Write(HitstopTicks);
        writer.Write(RecoveryTicks);
        writer.Write(TimeSinceHit.Raw);
        writer.Write(GravityRestore.Raw);
        writer.Write(ExplosionPower.Raw);
        writer.Write(Owner);
        writer.Write(IngestedBy);
        writer.Write(ClaimTicks);
        writer.Write(SpawnTicks);
        writer.Write(DirectionTicks);
        writer.Write(X.Raw);
        writer.Write(Y.Raw);
        writer.Write(VX.Raw);
        writer.Write(VY.Raw);
        writer.Write(TargetVX.Raw);
        writer.Write(TargetVY.Raw);
    }

    internal static FlyState ReadSnapshot(BinaryReader reader) =>
        new()
        {
            Active = reader.ReadBoolean(),
            Phase = (FlyPhase)reader.ReadInt32(),
            HasBounced = reader.ReadBoolean(),
            HitsTaken = reader.ReadInt32(),
            LastHitBy = reader.ReadInt32(),
            HitstopTicks = reader.ReadInt32(),
            RecoveryTicks = reader.ReadInt32(),
            TimeSinceHit = new(reader.ReadInt64()),
            GravityRestore = new(reader.ReadInt64()),
            ExplosionPower = new(reader.ReadInt64()),
            Owner = reader.ReadInt32(),
            IngestedBy = reader.ReadInt32(),
            ClaimTicks = reader.ReadInt32(),
            SpawnTicks = reader.ReadInt32(),
            DirectionTicks = reader.ReadInt32(),
            X = new(reader.ReadInt64()),
            Y = new(reader.ReadInt64()),
            VX = new(reader.ReadInt64()),
            VY = new(reader.ReadInt64()),
            TargetVX = new(reader.ReadInt64()),
            TargetVY = new(reader.ReadInt64()),
        };
}
