using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace FrogSmashers.Client;

internal sealed class SpatialSoundInstance : IDisposable
{
    private readonly SpatialSoundData data;
    private readonly Vector2 gains;
    private readonly byte[] chunk;
    private readonly object sync = new();
    private int frame;
    private bool disposed;
    public DynamicSoundEffectInstance Instance { get; }

    public SpatialSoundInstance(SpatialSoundData data, Vector2 gains)
    {
        this.data = data;
        this.gains = gains;
        chunk = new byte[Math.Max(1, data.SampleRate / 20) * 4];
        Instance = new(data.SampleRate, AudioChannels.Stereo);
        Instance.BufferNeeded += Fill;
        try
        {
            Fill(this, EventArgs.Empty);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void Fill(object? sender, EventArgs args)
    {
        lock (sync)
        {
            if (disposed)
                return;
            while (frame < data.Frames && Instance.PendingBufferCount < 4)
            {
                int frames = data.Render(frame, gains, chunk);
                Instance.SubmitBuffer(chunk, 0, frames * 4);
                frame += frames;
            }
        }
    }

    public void Update()
    {
        lock (sync)
        {
            if (!disposed && frame == data.Frames && Instance.PendingBufferCount == 0)
                Instance.Stop();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            Instance.BufferNeeded -= Fill;
            Instance.Dispose();
        }
    }
}
