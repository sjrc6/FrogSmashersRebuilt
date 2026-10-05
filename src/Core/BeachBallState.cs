namespace FrogSmashers.Core;

public enum BeachBallPhase
{
    Flying,
    Settling,
    Passive,
    Resting,
}

public sealed class BeachBallState
{
    public static readonly Fixed Radius = Fixed.FromDecimal(1.1m);
    public bool Active;
    public BeachBallPhase Phase;
    public bool HasReachedApex;
    public int SettlingTicks;
    public int HitsTaken;
    public int LastHitBy = -1;
    public int HitstopTicks;
    public Fixed TimeSinceHit;
    public Fixed GravityRestore;
    public int ResetTicks;
    public Fixed X;
    public Fixed Y;
    public Fixed VX;
    public Fixed VY;
    public FixedVector Position => new(X, Y);

    internal void WriteSnapshot(BinaryWriter writer)
    {
        writer.Write(Active);
        writer.Write((byte)Phase);
        writer.Write(HasReachedApex);
        writer.Write(SettlingTicks);
        writer.Write(HitsTaken);
        writer.Write(LastHitBy);
        writer.Write(HitstopTicks);
        writer.Write(TimeSinceHit.Raw);
        writer.Write(GravityRestore.Raw);
        writer.Write(ResetTicks);
        writer.Write(X.Raw);
        writer.Write(Y.Raw);
        writer.Write(VX.Raw);
        writer.Write(VY.Raw);
    }

    internal static BeachBallState ReadSnapshot(BinaryReader reader) =>
        new()
        {
            Active = reader.ReadBoolean(),
            Phase = (BeachBallPhase)reader.ReadByte(),
            HasReachedApex = reader.ReadBoolean(),
            SettlingTicks = reader.ReadInt32(),
            HitsTaken = reader.ReadInt32(),
            LastHitBy = reader.ReadInt32(),
            HitstopTicks = reader.ReadInt32(),
            TimeSinceHit = new(reader.ReadInt64()),
            GravityRestore = new(reader.ReadInt64()),
            ResetTicks = reader.ReadInt32(),
            X = new(reader.ReadInt64()),
            Y = new(reader.ReadInt64()),
            VX = new(reader.ReadInt64()),
            VY = new(reader.ReadInt64()),
        };
}
