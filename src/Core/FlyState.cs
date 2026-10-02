namespace FrogSmashers.Core;

public sealed class FlyState
{
    public bool Active;
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
