using Microsoft.Xna.Framework.Audio;

namespace FrogSmashers.Client;

public sealed class DynamicFlightLoop : IDisposable
{
    private readonly DynamicSoundEffectInstance instance;
    private readonly LoopResampler resampler;
    private readonly byte[] chunk;
    private readonly object sync = new();

    public DynamicFlightLoop(DecodedSound data)
    {
        resampler = new(data);
        instance = new(data.SampleRate, (AudioChannels)data.Channels);

        chunk = new byte[Math.Max(1, data.SampleRate / 100) * data.Channels * 2];
        instance.BufferNeeded += Fill;
    }

    public SoundState State => instance.State;
    public float Volume
    {
        get => instance.Volume;
        set => instance.Volume = Math.Clamp(value, 0, 1);
    }

    public float Rate
    {
        get
        {
            lock (sync)
            {
                return resampler.Rate;
            }
        }
        set
        {
            lock (sync)
            {
                resampler.Rate = value;
            }
        }
    }

    private void Fill(object? sender, EventArgs args)
    {
        lock (sync)
        {
            while (instance.PendingBufferCount < 3)
            {
                resampler.Render(chunk);
                instance.SubmitBuffer(chunk);
            }
        }
    }

    public void Play()
    {
        Fill(this, EventArgs.Empty);
        instance.Play();
    }

    public void Pause()
    {
        lock (sync)
        {
            if (instance.State == SoundState.Playing)
            {
                instance.Pause();
            }
        }
    }

    public void Resume()
    {
        lock (sync)
        {
            if (instance.State == SoundState.Paused)
            {
                instance.Resume();
            }
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            instance.Stop();
            resampler.Reset();
        }
    }

    public void Dispose()
    {
        instance.BufferNeeded -= Fill;
        instance.Dispose();
    }
}
