namespace FrogSmashers.Core;

public sealed class ParticleEmitterData
{
    public string Name { get; set; } = "";
    public string TexturePath { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Rate { get; set; }
    public float Lifetime { get; set; }
    public float Duration { get; set; }
    public float Speed { get; set; }
    public float Radius { get; set; }
    public float ConeAngle { get; set; }
    public float[] Size { get; set; } = [1, 1];
    public float[] Growth { get; set; } = [1, 1];
    public float GrowthStartTime { get; set; }
    public float GrowthEndTime { get; set; } = 1;
    public float Rotation { get; set; }
    public float[] Color { get; set; } = [1, 1, 1, 1];
    public float[] Right { get; set; } = [1, 0, 0];
    public float[] Up { get; set; } = [0, 1, 0];
    public float[] Forward { get; set; } = [0, 0, 1];
    public float[] EmitterScale { get; set; } = [1, 1, 1];
    public float[] LocalVelocity { get; set; } = [0, 0, 0];
    public float NoiseStrength { get; set; }
    public float NoiseFrequency { get; set; }
    public bool NoiseDamping { get; set; } = true;
    public bool Prewarm { get; set; }
    public int Order { get; set; }
}
