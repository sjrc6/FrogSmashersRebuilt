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
}
