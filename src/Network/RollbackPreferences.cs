using System.Globalization;
using FrogSmashers.Core;
using GGCS;

namespace FrogSmashers.Network;

public sealed record RollbackPreferences
{
    public const int PredictionFrames = World.TickRate / 4;
    public const int HistoryFrames = World.TickRate * 3;
    public const int MaximumDelayFrames = World.TickRate - 1;
    public const int MaximumExtraDelayFrames = World.TickRate - 1;
    public const int InputCapacityFrames = MaximumDelayFrames + MaximumExtraDelayFrames;
    public int Delay { get; init; } = 2;
    public int Donation { get; init; }
    public int MaxExtraDelay { get; init; }

    public bool IsValid =>
        Delay is >= 0 and <= MaximumDelayFrames
        && Donation is >= 0 and <= PredictionFrames
        && MaxExtraDelay is >= 0 and <= MaximumExtraDelayFrames;

    public RollbackPreferences Normalize() =>
        this with
        {
            Delay = Math.Clamp(Delay, 0, MaximumDelayFrames),
            Donation = Math.Clamp(Donation, 0, PredictionFrames),
            MaxExtraDelay = Math.Clamp(MaxExtraDelay, 0, MaximumExtraDelayFrames),
        };

    public RollbackTiming Timing =>
        new()
        {
            DelayFrames = Delay,
            DonationFrames = Donation,
            MaxExtraDelayFrames = MaxExtraDelay,
        };

    public static string Milliseconds(int frames)
    {
        decimal milliseconds = frames * 1000m / World.TickRate;
        string format =
            milliseconds < 10 ? "0.00"
            : milliseconds < 100 ? "0.0"
            : "0";
        return milliseconds.ToString(format, CultureInfo.InvariantCulture) + " MS";
    }
}
