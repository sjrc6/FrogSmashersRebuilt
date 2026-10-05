using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class BeachBallPresentation
{
    private const float OvalSpeed = 15;
    private const float TwistedSpeed = 35;
    private const float TurnSpeed = MathF.PI * 4;
    private const float SquishDuration = 1f / World.TickRate;
    private const float SquishAxisAlignment = .94f;
    private const float SquishImpactAlignment = .8f;
    private bool flying;
    private double phase;
    private double impactPhase;
    private int impactTicks;
    private int impactHits;
    private float flightRotation;
    private string? normalSprite;
    private float normalRotation;
    private string? squishSprite;
    private float squishRotation;
    private float squishRemaining;
    private long squishTick = -1;
    public string? Sprite;
    public float Rotation;
    public float Scale => 1;
    public readonly FlightPresentation Flight = new();

    private enum FlightStyle
    {
        Round,
        Oval,
        Twisted,
    }

    public void Update(Assets assets, BeachBallState ball, float dt)
    {
        UpdateAnimation(assets, ball, dt);
        normalSprite = Sprite;
        normalRotation = Rotation;
        if (squishRemaining > 0)
        {
            Sprite = squishSprite;
            Rotation = squishRotation;
            squishRemaining = Math.Max(0, squishRemaining - dt);
        }
    }

    public void Bounce(Assets assets, BeachBallState ball, SimulationEvent bounce, float age)
    {
        if (age >= SquishDuration || bounce.SurfaceSide == 0)
            return;
        if (normalSprite == null)
            Update(assets, ball, 0);
        var normal = Math.Abs(bounce.SurfaceSide) == 1 ? Vector2.UnitX : Vector2.UnitY;
        var velocity = new Vector2(bounce.VelocityX.ToFloat(), bounce.VelocityY.ToFloat());
        float speed = velocity.Length();
        float impact = MathF.Abs(Vector2.Dot(velocity, normal));
        var axis = new Vector2(-MathF.Sin(normalRotation), MathF.Cos(normalRotation));
        if (
            impact <= 5
            || impact < speed * SquishImpactAlignment
            || MathF.Abs(Vector2.Dot(axis, normal)) < SquishAxisAlignment
        )
            return;
        squishSprite = normalSprite + "-squish";
        squishRotation = normalRotation;
        squishRemaining = SquishDuration - Math.Max(0, age);
        squishTick = bounce.Tick;
    }

    public void ClearSquish()
    {
        squishRemaining = 0;
        squishTick = -1;
        Sprite = normalSprite;
        Rotation = normalRotation;
    }

    public void Rewind(long fromTick)
    {
        if (squishTick >= fromTick)
            ClearSquish();
    }

    private void UpdateAnimation(Assets assets, BeachBallState ball, float dt)
    {
        if (ball.HitstopTicks > 0)
        {
            UpdateImpact(assets, ball, dt);
            return;
        }
        impactTicks = 0;
        if (ball.Phase == BeachBallPhase.Resting)
        {
            Sprite = assets.Frame("beachBallIdle", 0);
            Rotation = 0;
            flying = false;
            phase = 0;
            return;
        }

        var velocity = new Vector2(ball.VX.ToFloat(), ball.VY.ToFloat());
        float speed = velocity.Length();
        var style =
            speed >= TwistedSpeed ? FlightStyle.Twisted
            : speed >= OvalSpeed ? FlightStyle.Oval
            : FlightStyle.Round;
        if (!flying)
        {
            flightRotation = speed > 0 ? MathF.Atan2(velocity.Y, velocity.X) - MathF.PI / 2 : 0;
            flying = true;
        }

        if (style != FlightStyle.Round && speed > 0)
        {
            float target = MathF.Atan2(velocity.Y, velocity.X) - MathF.PI / 2;
            float turn = MathHelper.WrapAngle(target - flightRotation);
            // The oval has an axis, not a forward end. Reversals must not flip the spin axis.
            if (turn > MathF.PI / 2)
                turn -= MathF.PI;
            else if (turn < -MathF.PI / 2)
                turn += MathF.PI;
            flightRotation = MathHelper.WrapAngle(flightRotation + Math.Clamp(turn, -TurnSpeed * dt, TurnSpeed * dt));
        }

        var (animation, delay) = style switch
        {
            FlightStyle.Round => ("beachBallFlightRound", RoundFrameDelay(speed)),
            FlightStyle.Oval => ("beachBallFlight", .04f),
            _ => ("beachBallFlightTwisted", .03f),
        };
        int frameCount = assets.Data.Animations[animation].Length;
        phase = (phase + dt / ((double)delay * frameCount)) % 1;
        Sprite = assets.Frame(animation, (int)(phase * frameCount));
        Rotation = flightRotation;
    }

    private void UpdateImpact(Assets assets, BeachBallState ball, float dt)
    {
        if (impactTicks == 0 || ball.HitsTaken != impactHits || ball.HitstopTicks > impactTicks)
            impactPhase = 0;
        impactTicks = ball.HitstopTicks;
        impactHits = ball.HitsTaken;
        float delay =
            impactHits <= 2 ? .1f
            : impactHits <= 4 ? .08f
            : .06f;
        int frameCount = assets.Data.Animations["beachBallImpact"].Length;
        impactPhase = (impactPhase + dt / ((double)delay * frameCount)) % 1;
        Sprite = assets.Frame("beachBallImpact", (int)(impactPhase * frameCount));
        Rotation = 0;
    }

    private static float RoundFrameDelay(float speed) =>
        speed switch
        {
            < 2.5f => .25f,
            < 5 => .1f,
            < 10 => .08f,
            _ => .05f,
        };
}
