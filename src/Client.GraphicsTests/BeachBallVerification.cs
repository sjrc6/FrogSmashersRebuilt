using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyBeachBall(string? directory)
    {
        VerifyBeachBallAnimation();
        VerifyBeachBallImpact();
        VerifyBeachBallSquish();
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
        if (
            !renderer.Effects.Active.Any(effect =>
                effect.Owner == 8
                && effect.Sprite == "beach-ball-twisted-01"
                && MathF.Abs(effect.SpriteScale - new BeachBallPresentation().Scale) < .001f
            )
        )
            throw new InvalidOperationException(
                "Ball silhouette must use the current sprite and scale through the trail shader"
            );
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
        foreach (int side in new[] { -2, -1, 1, 2 })
        {
            renderer.Reset();
            ball.VX = Math.Abs(side) == 1 ? 60 : 0;
            ball.VY = Math.Abs(side) == 2 ? 60 : 0;
            renderer.Update(.02f);
            renderer.DrawWorld(world, null, 1);
            renderer.Consume(
                [
                    new SimulationEvent(
                        10,
                        0,
                        SimulationEventKind.BeachBallBounce,
                        -1,
                        -1,
                        ball.X,
                        ball.Y,
                        0,
                        VelocityX: ball.VX,
                        VelocityY: ball.VY,
                        SurfaceSide: side
                    ),
                ],
                world
            );
            renderer.Update(.005f);
            renderer.DrawWorld(world, null, 1);
            if (!renderer.Effects.Active.Any(effect => effect.Owner == 8 && effect.Sprite!.EndsWith("-squish")))
                throw new InvalidOperationException("Rendered ball trails must follow the bounce squish silhouette");
            device.SetRenderTarget(null);
            renderer.Frame.GetData(before);
            renderer.Update(0);
            renderer.DrawWorld(world, null, 1);
            device.SetRenderTarget(null);
            renderer.Frame.GetData(after);
            if (!before.SequenceEqual(after))
                throw new InvalidOperationException("Pausing must freeze the bounce squish");
            if (directory != null)
            {
                using var output = File.Create(Path.Combine(directory, $"beach-ball-squish-{side}.png"));
                renderer.Frame.SaveAsPng(output, Width, Height);
            }
            renderer.Update(.005f);
            renderer.DrawWorld(world, null, 1);
            renderer.Update(.005f);
            renderer.DrawWorld(world, null, 1);
            if (renderer.Effects.Active.Any(effect => effect.Owner == 8 && effect.Sprite!.EndsWith("-squish")))
                throw new InvalidOperationException("Rendered squish silhouette must expire after one tick");
        }
        renderer.Reset();
        if (directory != null)
        {
            foreach (
                var (name, speed, hitstop, resting) in new[]
                {
                    ("idle", 0, 0, true),
                    ("round", 10, 0, false),
                    ("normal", 20, 0, false),
                    ("twisted", 80, 0, false),
                    ("impact", 80, 10, false),
                }
            )
            {
                renderer.Reset();
                ball.VX = speed;
                ball.VY = 0;
                ball.Phase = resting ? BeachBallPhase.Resting : BeachBallPhase.Flying;
                ball.HitstopTicks = hitstop;
                renderer.Update(.12f);
                renderer.DrawWorld(world, null, 1);
                device.SetRenderTarget(null);
                using var output = File.Create(Path.Combine(directory, $"beach-ball-{name}.png"));
                renderer.Frame.SaveAsPng(output, Width, Height);
            }
            renderer.Reset();
        }
        return
        [
            "Ball squishes one tick on aligned direct bounces, preserving spin frames across all animation types; glancing hits and rollback are handled",
            "Ball impact loops eight standing-view frames at 100/80/60ms for hits 1–2/3–4/5+, pauses and restarts on a new hit",
            "Ball uses native frog pixel scale, exact 15/35 shape cutoffs and 250/100/80/50ms round speed bands while preserving spin",
            "Ball shares hit flashes, stars, camera shake, bounce puffs, particles, smoke and trail shader; pause and rewind agree",
        ];
    }

    private void VerifyBeachBallSquish()
    {
        SimulationEvent Bounce(int side, int vx, int vy) =>
            new(
                10,
                0,
                SimulationEventKind.BeachBallBounce,
                -1,
                -1,
                12,
                4,
                0,
                VelocityX: vx,
                VelocityY: vy,
                SurfaceSide: side
            );
        foreach (
            var (speed, resting, hitstop, name) in new[]
            {
                (0, true, 0, "beach-ball-idle"),
                (10, false, 0, "beach-ball-round-01"),
                (25, false, 0, "beach-ball-flight-01"),
                (60, false, 0, "beach-ball-twisted-01"),
                (0, false, 10, "beach-ball-impact-01"),
            }
        )
        {
            var ball = new BeachBallState
            {
                VY = speed,
                Phase = resting ? BeachBallPhase.Resting : BeachBallPhase.Flying,
                HitstopTicks = hitstop,
                HitsTaken = 3,
            };
            var pose = new BeachBallPresentation();
            pose.Update(assets, ball, 0);
            pose.Bounce(assets, ball, Bounce(-2, 0, -20), 0);
            foreach (float dt in new[] { .005f, 0, .005f })
            {
                pose.Update(assets, ball, dt);
                if (pose.Sprite != name + "-squish")
                    throw new InvalidOperationException(
                        $"Squish must preserve the current {name} spin frame for one tick"
                    );
            }
            pose.Update(assets, ball, .001f);
            if (pose.Sprite != name)
                throw new InvalidOperationException("Squish lasted longer than one 10ms tick");
            pose.Bounce(assets, ball, Bounce(-2, 0, -20), .01f);
            pose.Update(assets, ball, .001f);
            if (pose.Sprite != name)
                throw new InvalidOperationException("An expired bounce must not create a fresh squish");
            pose.Bounce(assets, ball, Bounce(-2, 0, -20), 0);
            pose.Update(assets, ball, .001f);
            pose.Rewind(10);
            if (pose.Sprite != name)
                throw new InvalidOperationException("Rollback left a squish from a discarded collision");
        }
        foreach (int side in new[] { -2, -1, 1, 2 })
        {
            var ball = new BeachBallState { VX = Math.Abs(side) == 1 ? 60 : 0, VY = Math.Abs(side) == 2 ? 60 : 0 };
            var pose = new BeachBallPresentation();
            pose.Update(assets, ball, .06f);
            string frame = pose.Sprite!;
            float rotation = pose.Rotation;
            pose.Bounce(assets, ball, Bounce(side, (int)ball.VX.ToFloat(), (int)ball.VY.ToFloat()), 0);
            pose.Update(assets, ball, .01f);
            if (pose.Sprite != frame + "-squish" || pose.Rotation != rotation)
                throw new InvalidOperationException(
                    "Every aligned wall/floor/ceiling bounce must preserve spin and orientation"
                );
        }
        var roundBall = new BeachBallState { VY = 10 };
        var roundPose = new BeachBallPresentation();
        roundPose.Update(assets, roundBall, 0);
        foreach (var bounce in new[] { Bounce(1, 20, 0), Bounce(-2, 80, -10), Bounce(-2, 0, -5) })
        {
            roundPose.Bounce(assets, roundBall, bounce, 0);
            roundPose.Update(assets, roundBall, .001f);
            if (roundPose.Sprite!.EndsWith("-squish"))
                throw new InvalidOperationException(
                    "Misaligned, glancing and tiny bounces must not squish the round ball"
                );
        }
        roundPose.Bounce(assets, roundBall, Bounce(-2, 0, -10), 0);
        roundPose.Update(assets, roundBall, .001f);
        roundPose.ClearSquish();
        if (roundPose.Sprite!.EndsWith("-squish"))
            throw new InvalidOperationException("A new bat/tongue hit must cancel a pending bounce squish");
    }

    private void VerifyBeachBallImpact()
    {
        foreach (int hits in new[] { 0, 1, 2, 3, 4, 5, 8 })
        {
            var pose = new BeachBallPresentation();
            var ball = new BeachBallState
            {
                HitstopTicks = 100,
                HitsTaken = hits,
                VX = 80,
                VY = 20,
            };
            void Expect(int frame, float dt)
            {
                pose.Update(assets, ball, dt);
                if (pose.Sprite != $"beach-ball-impact-{frame:00}" || pose.Rotation != 0)
                    throw new InvalidOperationException(
                        $"Impact at {hits} hits expected frame {frame}, got {pose.Sprite}"
                    );
            }
            float delay =
                hits <= 2 ? .1f
                : hits <= 4 ? .08f
                : .06f;
            Expect(1, 0);
            Expect(1, delay / 2);
            Expect(2, delay / 2);
            for (int frame = 3; frame <= 8; frame++)
                Expect(frame, delay);
            Expect(1, delay);
            Expect(4, delay * 3 + .001f);
            Expect(4, 0);
            ball.HitstopTicks--;
            Expect(4, 0);
            ball.HitstopTicks = 100;
            Expect(1, 0);
            Expect(2, delay);
            ball.HitsTaken++;
            Expect(1, 0);
            ball.HitstopTicks = 0;
            ball.Phase = BeachBallPhase.Resting;
            pose.Update(assets, ball, 0);
            if (pose.Sprite != "beach-ball-idle")
                throw new InvalidOperationException("Rest must return to the static standing sprite");
        }
    }

    private void VerifyBeachBallAnimation()
    {
        var ball = new BeachBallState { Phase = BeachBallPhase.Resting };
        var pose = new BeachBallPresentation();
        void Expect(string sprite, float dt)
        {
            pose.Update(assets, ball, dt);
            if (pose.Sprite != sprite)
                throw new InvalidOperationException($"Ball animation expected {sprite}, got {pose.Sprite}");
        }

        Expect("beach-ball-idle", 1);
        ball.Phase = BeachBallPhase.Flying;
        foreach (
            var (speed, prefix, delay) in new[]
            {
                (0, "beach-ball-round", .25f),
                (2, "beach-ball-round", .25f),
                (3, "beach-ball-round", .1f),
                (5, "beach-ball-round", .08f),
                (9, "beach-ball-round", .08f),
                (10, "beach-ball-round", .05f),
                (14, "beach-ball-round", .05f),
                (20, "beach-ball-flight", .04f),
                (80, "beach-ball-twisted", .03f),
            }
        )
        {
            foreach (int hits in new[] { 0, 1, 8 })
            {
                pose = new BeachBallPresentation();
                ball.VX = speed;
                ball.HitsTaken = hits;
                Expect(prefix + "-01", 0);
                Expect(prefix + "-01", delay / 2);
                Expect(prefix + "-02", delay / 2);
                for (int frame = 3; frame <= 16; frame++)
                    Expect($"{prefix}-{frame:00}", delay);
                Expect(prefix + "-01", delay);
                Expect(prefix + "-04", delay * 3 + .001f);
                Expect(prefix + "-04", 0);
            }
        }
        ball.HitstopTicks = 10;
        Expect("beach-ball-impact-02", .1f);
        if (pose.Rotation != 0)
            throw new InvalidOperationException("Ball impact spin must retain the standing camera orientation");
        ball.HitstopTicks = 0;
        Expect("beach-ball-twisted-04", 0);
        ball.VX = 20;
        Expect("beach-ball-flight-04", 0);
        ball.VX = 10;
        Expect("beach-ball-round-04", 0);
        Expect("beach-ball-round-05", .05f);
        ball.Phase = BeachBallPhase.Resting;
        Expect("beach-ball-idle", 1);

        ball.Phase = BeachBallPhase.Flying;
        ball.VX = 0;
        ball.VY = 20;
        pose = new BeachBallPresentation();
        Expect("beach-ball-flight-01", 0);
        foreach (int verticalSpeed in new[] { 10, 0, -10, -20 })
        {
            ball.VY = verticalSpeed;
            pose.Update(assets, ball, .01f);
            if (MathF.Abs(pose.Rotation) > .001f)
                throw new InvalidOperationException("A vertical apex must preserve the ball's spin axis");
        }

        foreach (var (vx, vy) in new[] { (80, 0), (0, 80), (80, 20), (20, 80) })
        {
            pose = new BeachBallPresentation();
            ball.VX = vx;
            ball.VY = vy;
            pose.Update(assets, ball, 0);
            float before = pose.Rotation;
            ball.VX = -vx;
            ball.VY = -vy;
            pose.Update(assets, ball, .03f);
            if (MathF.Abs(MathHelper.WrapAngle(pose.Rotation - before)) > .001f)
                throw new InvalidOperationException("A reversed velocity must not reverse the ball's spin axis");
            if (pose.Sprite != "beach-ball-twisted-02")
                throw new InvalidOperationException("Bouncing must preserve forward spin animation");
        }
        ball.VX = 80;
        ball.VY = 20;
        pose = new BeachBallPresentation();
        pose.Update(assets, ball, 0);
        foreach (var (vx, vy) in new[] { (-80, 20), (-80, -20), (80, -20) })
        {
            float before = pose.Rotation;
            ball.VX = vx;
            ball.VY = vy;
            pose.Update(assets, ball, .01f);
            if (MathF.Abs(MathHelper.WrapAngle(pose.Rotation - before)) > MathF.PI * 4 * .01f + .001f)
                throw new InvalidOperationException("Angled wall/floor bounces must turn smoothly");
        }

        ball.VY = 0;
        ball.VX = 20;
        pose = new BeachBallPresentation();
        Expect("beach-ball-flight-01", 0);
        foreach (
            var (speed, prefix) in new[]
            {
                (14, "round"),
                (15, "flight"),
                (34, "flight"),
                (35, "twisted"),
                (34, "flight"),
                (14, "round"),
            }
        )
        {
            ball.VX = speed;
            Expect($"beach-ball-{prefix}-01", 0);
        }
        ball.VX = Fixed.FromDecimal(2.5m);
        Expect("beach-ball-round-02", .1f);
        ball.VX = 1;
        Expect("beach-ball-round-03", .25f);
        ball.VX = 5;
        Expect("beach-ball-round-04", .08f);
        ball.VX = 10;
        Expect("beach-ball-round-05", .05f);

        var frogSprite = assets.Data.Sprites[assets.Frame("idle", 0)!];
        var ballSprite = assets.Data.Sprites[assets.Frame("beachBallIdle", 0)!];
        float ballPixelSize = pose.Scale / ballSprite.PixelsPerUnit;
        if (
            Math.Abs(ballPixelSize - 1f / frogSprite.PixelsPerUnit) > .00001f
            || Math.Abs(ballPixelSize * 22 - BeachBallState.Radius.ToFloat() * 2) > .00001f
        )
            throw new InvalidOperationException(
                "Ball pixels must match frog pixels and its standing silhouette must match the collider"
            );
    }
}
