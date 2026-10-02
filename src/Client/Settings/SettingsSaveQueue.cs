namespace FrogSmashers.Client;

internal sealed class SettingsSaveQueue(Action save)
{
    private double remaining;
    private bool pending;

    public void Request()
    {
        pending = true;
        remaining = .5;
    }

    public void Update(double elapsed)
    {
        if (!pending)
            return;
        remaining -= elapsed;
        if (remaining <= 0)
            Flush();
    }

    public void Flush()
    {
        if (!pending)
            return;
        save();
        pending = false;
    }
}
