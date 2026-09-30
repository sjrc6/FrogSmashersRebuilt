using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class GameCamera
{
    public const int Width = 1280;
    public const int Height = 720;
    private readonly Dictionary<string, float> parameters;
    private Vector2 velocity;
    public Vector2 Position;
    public float HalfHeight = 18;
    public bool ShakeEnabled { get; set; } = true;
    public float PixelsPerUnit => Height / (HalfHeight * 2);
    public Vector3 ListenerPosition => new(Position, -10);

    public GameCamera(Dictionary<string, float> parameters) => this.parameters = parameters;

    public void Reset(MapData? map = null)
    {
        Position = map == null ? Vector2.Zero : new Vector2(map.CameraX, map.CameraY);
        HalfHeight = map?.OrthoSize ?? 18;
        velocity = Vector2.Zero;
    }

    public Vector2 ToScreen(Vector2 position) =>
        new(
            (position.X - Position.X) * PixelsPerUnit + Width / 2,
            Height / 2 - (position.Y - Position.Y) * PixelsPerUnit
        );

    public void Shake(Vector2 direction, float intensity)
    {
        if (ShakeEnabled)
        {
            velocity += direction * (parameters.GetValueOrDefault("shakePower", 20) * intensity + 10);
        }
    }

    public void Update(World world, float dt, float elapsed)
    {
        if (world.Rules.Lobby)
        {
            Reset(world.Map);
            return;
        }
        float finishAge =
            world.Phase == MatchPhase.RoundFinished
                ? (world.Rules.RoundFinishTicks - world.PhaseTicks) / (float)World.TickRate
                : 0;
        if (world.Phase == MatchPhase.Playing || finishAge < 1.5f)
        {
            var p = Position + velocity * dt;
            if (p.X > 1 && velocity.X > 0)
            {
                velocity.X *= -.9f;
                p.X = 1;
            }

            if (p.Y > 1 && velocity.Y > 0)
            {
                velocity.Y *= -.9f;
                p.Y = 1;
            }

            if (p.X < -1 && velocity.X < 0)
            {
                velocity.X *= -.9f;
                p.X = -1;
            }

            if (p.Y < -1 && velocity.Y < 0)
            {
                velocity.Y *= -.9f;
                p.Y = -1;
            }

            float spring = parameters.GetValueOrDefault("cameraSpring", 500);
            if (MathF.Sign(velocity.X) == MathF.Sign(p.X))
            {
                velocity.X -= p.X * spring * dt;
            }

            if (MathF.Sign(velocity.Y) == MathF.Sign(p.Y))
            {
                velocity.Y -= p.Y * spring * dt;
            }

            var wobble = new Vector2(
                MathF.Sin(elapsed * parameters.GetValueOrDefault("cameraWobbleSpeedX"))
                    * parameters.GetValueOrDefault("cameraWobbleAmountX"),
                MathF.Sin(elapsed * parameters.GetValueOrDefault("cameraWobbleSpeedY"))
                    * parameters.GetValueOrDefault("cameraWobbleAmountY")
            );
            Position = Vector2.Lerp(p, ShakeEnabled ? wobble : Vector2.Zero, Math.Clamp(dt * 3, 0, 1));
        }
        else if (world.Phase == MatchPhase.RoundFinished && world.Winner >= 0)
        {
            var player = world.Players[world.Winner];
            if (player.Alive)
            {
                Position = Vector2.Lerp(
                    Position,
                    new(player.Center.X.ToFloat(), player.Center.Y.ToFloat()),
                    Math.Clamp(dt * 5, 0, 1)
                );
                HalfHeight = MathHelper.Lerp(HalfHeight, 7.5f, Math.Clamp(dt * 2, 0, 1));
                float extent = HalfHeight * Width / Height;
                Position.Y = Math.Clamp(Position.Y, -(18 - HalfHeight), 18 - HalfHeight);
                Position.X = Math.Clamp(Position.X, -(32 - extent), 32 - extent);
            }
            else
            {
                HalfHeight = MathHelper.Lerp(HalfHeight, 18, Math.Clamp(dt, 0, 1));
                Position = Vector2.Lerp(Position, Vector2.Zero, Math.Clamp(dt, 0, 1));
            }
        }
    }
}
