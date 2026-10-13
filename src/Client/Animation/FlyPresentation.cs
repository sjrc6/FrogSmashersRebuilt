using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class FlyPresentation
{
    private readonly SpriteAnimation wings = new();
    private float squishRemaining;
    private long squishTick = -1;
    private Vector2 squishScale = Vector2.One;
    public string? Sprite { get; private set; }
    public float Rotation { get; private set; }
    public Vector2 Scale { get; private set; } = Vector2.One;
    public FlightPresentation Flight { get; } = new();

    public void Update(Assets assets, FlyState fly, float dt)
    {
        var animation = assets.Data.Effects["Fly"];
        if (fly.Phase == FlyPhase.Flying)
        {
            wings.Step(dt, animation.FrameSeconds, animation.Frames.Length, false);
            Sprite =
                wings.Frame < 0 ? animation.InitialSpriteId : animation.Frames[wings.Frame % animation.Frames.Length];
            Rotation = 0;
        }
        else
        {
            Sprite = "fly-tumble";
            Rotation =
                fly.Phase == FlyPhase.Tumbling && (fly.VX != 0 || fly.VY != 0)
                    ? MathF.Atan2(fly.VY.ToFloat(), fly.VX.ToFloat()) - MathF.PI / 2
                    : 0;
        }
        Scale = squishRemaining > 0 ? squishScale : Vector2.One;
        squishRemaining = Math.Max(0, squishRemaining - dt);
    }

    public void Bounce(SimulationEvent bounce, float age)
    {
        float duration = 1f / World.TickRate;
        if (bounce.Strength <= 5 || age >= duration)
            return;
        squishScale = Math.Abs(bounce.SurfaceSide) == 1 ? new(.6f, 1.3f) : new(1.3f, .6f);
        squishRemaining = duration - Math.Max(0, age);
        squishTick = bounce.Tick;
    }

    public void ClearSquish()
    {
        squishRemaining = 0;
        squishTick = -1;
        Scale = Vector2.One;
    }

    public void Rewind(long fromTick)
    {
        if (squishTick >= fromTick)
            ClearSquish();
    }
}
