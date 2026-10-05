using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class FlightPresentation
{
    public float ParticleCounter;
    public float TrailCounter;
    public float TrailFaderCounter;
    public int TrailNumber;
    public Vector2 LastSmoke;
}

internal readonly record struct FlightVisual
{
    public int Owner { get; init; }
    public long Tick { get; init; }
    public int Hits { get; init; }
    public Vector2 Center { get; init; }
    public Vector2 Velocity { get; init; }
    public Vector2 SpritePosition { get; init; }
    public string? Sprite { get; init; }
    public float Rotation { get; init; }
    public int Facing { get; init; }
    public Color Color { get; init; }
    public Color? AttackerColor { get; init; }
    public bool Airborne { get; init; }
    public bool Silhouette { get; init; }
    public bool Frozen { get; init; }
    public bool Dodged { get; init; }
    public bool Recovered { get; init; }
    public float SquareSize { get; init; }
}
