using System.Diagnostics;

namespace FrogSmashers.Client;

internal sealed class FrameTiming
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double elapsedSeconds;
    private int frames;
    public float FramesPerSecond { get; private set; }

    public void RecordFrame(double seconds)
    {
        elapsedSeconds += seconds;
        frames++;
        if (elapsedSeconds < 0.5)
        {
            return;
        }

        FramesPerSecond = (float)(frames / elapsedSeconds);
        elapsedSeconds = 0;
        frames = 0;
    }

    public void FinishFrame(int? frameLimit)
    {
        if (frameLimit.HasValue)
        {
            double frameSeconds = 1.0 / frameLimit.Value;
            double remaining = frameSeconds - clock.Elapsed.TotalSeconds;
            if (remaining > 0.001)
            {
                Thread.Sleep(TimeSpan.FromSeconds(remaining - 0.0005));
            }

            while (clock.Elapsed.TotalSeconds < frameSeconds)
            {
                Thread.SpinWait(32);
            }
        }

        clock.Restart();
    }
}
