namespace FrogSmashers.Client;

internal sealed class MenuAdjustRepeat
{
    private int direction;
    private double remaining;

    public int Read(int held, int pressed, double elapsedSeconds)
    {
        if (held == 0)
        {
            Reset();
            return pressed;
        }
        if (pressed != 0)
        {
            direction = held;
            remaining = .35;
            return pressed;
        }
        if (held != direction)
        {
            Reset();
            return 0;
        }
        remaining -= elapsedSeconds;
        if (remaining > 0)
            return 0;
        remaining = Math.Max(remaining + .075, 0);
        return direction;
    }

    public void Reset()
    {
        direction = 0;
        remaining = 0;
    }
}
