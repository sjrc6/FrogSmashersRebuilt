namespace FrogSmashers.Core;

public sealed class SoundSettingsData
{
    public float Volume { get; set; } = 1;
    public float PitchVariance { get; set; } = .1f;
}

public sealed class SceneAudioData
{
    public string Path { get; set; } = "";
    public string ObjectPath { get; set; } = "";
    public float Volume { get; set; } = 1;
    public float Pitch { get; set; } = 1;
    public bool Loop { get; set; }
    public bool PlayOnAwake { get; set; }
    public bool Active { get; set; }
    public float SpatialBlend { get; set; }
    public float Pan { get; set; }
    public float MinDistance { get; set; } = 1;
    public float MaxDistance { get; set; } = 500;
    public int RolloffMode { get; set; } = 1;
}
