namespace FrogSmashers.Client;

public sealed class SpriteAnimation
{
    public int Frame = -1;
    public float Counter;

    public bool Step(float dt, float delay, int count, bool once)
    {
        if (dt <= 0)
        {
            return once && Frame >= count;
        }

        Counter += dt;
        if (Counter > delay)
        {
            Counter -= delay;
            Frame++;
        }

        return once && Frame >= count;
    }
}
