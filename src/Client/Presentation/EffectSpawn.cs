using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal readonly record struct EffectSpawn()
{
    public float Age { get; init; }
    public float Scale { get; init; } = 1;
    public Vector2 Velocity { get; init; }
    public float Rotation { get; init; }
    public int Facing { get; init; } = 1;
    public float Life { get; init; }
    public Vector2 Stretch { get; init; } = Vector2.One;
    public int Points { get; init; }
    public bool CameraRelative { get; init; }
}
