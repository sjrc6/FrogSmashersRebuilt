using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyBeachBall(string? directory)
    {
        renderer.Reset();
        var map = assets.Data.PresentationScenes["Lobby"];
        if (
            map.Collision.Any(box => box.Name == "Room wall" && !box.BeachBallCollision)
            || map.Collision.Any(box => box.Name.Contains("boundary") && !box.BeachBallCollision)
            || map.Collision.Any(box =>
                (box.Name.Contains("platform") || box.Name == "Floor") && box.BeachBallCollision
            )
        )
            throw new InvalidOperationException(
                "Ball collision must retain walls/boundaries and omit interior platforms"
            );
        var world = new World(map, new GameRules(lobby: true, playerCount: 8));
        var ball = world.BeachBall;
        ball.X = 12;
        ball.Y = 4;
        ball.VX = 80;
        ball.VY = 20;
        ball.HitsTaken = 4;
        ball.LastHitBy = 0;
        renderer.Consume(
            [
                new SimulationEvent(
                    0,
                    0,
                    SimulationEventKind.BeachBallHit,
                    -1,
                    0,
                    12,
                    4,
                    80,
                    Power: 1,
                    ComboHits: 4,
                    VelocityX: 80,
                    VelocityY: 20,
                    HitstopSeconds: Fixed.FromDecimal(.3m),
                    HitEffectX: 12,
                    HitEffectY: 4
                ),
            ],
            world
        );
        renderer.Update(.02f);
        renderer.DrawWorld(world, null, 1);
        if (renderer.Camera.Position == new Vector2(map.CameraX, map.CameraY))
            throw new InvalidOperationException("Charged ball hits must shake the lobby camera");
        foreach (
            string effect in new[]
            {
                "HitEffect",
                "HitStar",
                "HitStarPowerHit",
                "FaderTrail",
                "HitParticle",
                "LineParticle",
                "SmokeRing",
                "SmokeRingBack",
            }
        )
            if (!renderer.Effects.Active.Any(item => item.Name == effect))
                throw new InvalidOperationException("Ball is missing the shared frog effect: " + effect);
        if (!renderer.Effects.Active.Any(effect => effect.Owner == 8 && effect.SquareSize > 0))
            throw new InvalidOperationException("Ball silhouette must use the placeholder through the trail shader");
        var before = new Color[Width * Height];
        var after = new Color[before.Length];
        device.SetRenderTarget(null);
        renderer.Frame.GetData(before);
        renderer.Update(0);
        renderer.DrawWorld(world, null, 1);
        device.SetRenderTarget(null);
        renderer.Frame.GetData(after);
        if (!before.SequenceEqual(after))
            throw new InvalidOperationException("Paused ball presentation changed pixels");
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
            using var output = File.Create(Path.Combine(directory, "beach-ball-flight.png"));
            renderer.Frame.SaveAsPng(output, Width, Height);
        }
        renderer.Rewind(0);
        if (renderer.Effects.Active.Count != 0)
            throw new InvalidOperationException("Rollback left stale ball effects");
        foreach (int side in new[] { -2, -1, 1, 2 })
        {
            renderer.Consume(
                [
                    new SimulationEvent(
                        1,
                        side + 2,
                        SimulationEventKind.BeachBallBounce,
                        -1,
                        -1,
                        12,
                        4,
                        0,
                        SurfaceSide: side
                    ),
                ],
                world
            );
            var puff = renderer.Effects.Active.Last();
            var contact = side switch
            {
                -2 => new Vector2(0, -1),
                2 => new Vector2(0, 1),
                _ => new Vector2(side, 0),
            };
            if (
                puff.Name != (Math.Abs(side) == 1 ? "WallPuff" : "BouncePuff")
                || Vector2.Distance(puff.Position, new Vector2(12, 4) + contact * BeachBallState.Radius.ToFloat())
                    > .001f
            )
                throw new InvalidOperationException("Ball bounce puff does not match its surface contact");
        }
        renderer.Reset();
        return
        [
            "Ball shares hit flashes, stars, camera shake, bounce puffs, particles, smoke and trail shader; pause and rewind agree",
        ];
    }
}
