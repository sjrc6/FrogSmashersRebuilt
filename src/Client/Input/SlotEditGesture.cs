namespace FrogSmashers.Client;

internal sealed class SlotEditGesture
{
    private bool armed;
    private bool adjusted;

    public void Reset() => armed = adjusted = false;

    public int Update(MenuInput input)
    {
        if (input.Accept)
            armed = true;
        if (!armed)
            return 0;
        if (input.AcceptHeld && input.Horizontal != 0)
        {
            adjusted = true;
            return input.Horizontal;
        }
        if (!input.AcceptReleased)
            return 0;
        int change = adjusted ? 0 : 1;
        Reset();
        return change;
    }
}
