namespace FrogSmashers.Client;

internal sealed class ToastController
{
    internal const double TransitionSeconds = .3;
    internal const double DisplaySeconds = 2;
    private double transition;
    private double remaining;
    private float previousStart;
    public string? Current { get; private set; }
    public string? Previous { get; private set; }
    private float Progress =>
        transition >= TransitionSeconds ? 1 : (1 - MathF.Pow(2, (float)(-5 * transition / TransitionSeconds)));
    public float CurrentVisibility => Current == null ? 0 : Progress;
    public float PreviousVisibility => Previous == null ? 0 : previousStart * (1 - Progress);

    public void Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (Current != null && CurrentVisibility > 0)
        {
            previousStart = CurrentVisibility;
            Previous = Current;
        }
        else
            previousStart = PreviousVisibility;
        Current = text;
        transition = 0;
        remaining = DisplaySeconds;
    }

    public void Update(double seconds)
    {
        transition = Math.Min(TransitionSeconds, transition + seconds);
        if (transition >= TransitionSeconds)
            Previous = null;
        if (Current == null)
            return;
        remaining -= seconds;
        if (remaining > 0)
            return;
        Previous = Current;
        previousStart = CurrentVisibility;
        Current = null;
        transition = 0;
    }
}
