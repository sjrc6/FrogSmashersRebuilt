using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifySmokeRings(string? directory = null)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }

        string Sprite(string name) => assets.Data.Sprites.Single(pair => pair.Value.Name == name).Key;
        string frog = Sprite("idle_1");

        var map = new MapData
        {
            Id = "smoke-ring-check",
            OrthoSize = 7.2f,
            BackgroundColor = [.25f, .25f, .25f, 1],
            Spawns = [new(), new()],
        };
        var world = new World(map, new GameRules(playerCount: 2));
        foreach (var player in world.Players)
            player.Alive = false;
        var launched = world.Players[0];
        launched.Mode = CharacterMode.Bouncing;
        launched.OnGround = false;
        launched.HitsTaken = 3;
        launched.VX = 30;
        launched.LastHitBy = -1;

        using var reference = new RenderTarget2D(
            device,
            Width,
            Height,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            4,
            RenderTargetUsage.DiscardContents
        );
        var expected = new Color[Width * Height];
        var actual = new Color[Width * Height];
        int[] sourceFrames = [3, 2, 1, 2, 3, 4, 5];

        foreach (float rotation in new[] { 0, MathF.PI / 2, .63f })
        {
            renderer.Reset();
            renderer.Update(0);
            renderer.DrawWorld(world, null, 1, false);
            var pose = new CharacterPresentation { Rotation = rotation, LastSmoke = new(100, 0) };
            renderer.Effects.UpdateCharacter(launched, world, new(0, -1), pose, 0);
            var rings = renderer.Effects.Active.Where(effect => effect.Name.StartsWith("SmokeRing")).ToArray();
            foreach (var other in renderer.Effects.Active.Except(rings))
                other.Dead = true;
            Check(rings.Length == 2, $"smoke ring at {rotation} spawns both halves from a launched frog");
            Check(
                rings.All(effect => effect.Position == Vector2.Zero && effect.Rotation == rotation)
                    && rings.Single(effect => effect.Name == "SmokeRing").Z == -3
                    && rings.Single(effect => effect.Name == "SmokeRingBack").Z == 3,
                $"smoke ring at {rotation} shares its transform and straddles frog depth"
            );

            for (int frame = 0; frame < sourceFrames.Length; frame++)
            {
                if (frame > 0)
                    renderer.Effects.Update(.051f, frame * .051f);

                foreach (bool withFrog in new[] { false, true })
                {
                    map.Sprites.Clear();
                    if (withFrog)
                        map.Sprites.Add(new() { SpriteId = frog });
                    renderer.DrawWorld(world, null, 1, false);
                    device.SetRenderTarget(null);
                    renderer.Frame.GetData(actual);

                    device.SetRenderTarget(reference);
                    device.Clear(EffectSystem.ToColor(map.BackgroundColor));
                    Begin(0);
                    renderer.Canvas.DrawSprite(
                        Sprite($"dust_circle_v2_{sourceFrames[frame]:00}_B"),
                        Vector2.Zero,
                        Color.White,
                        new(.8f),
                        rotation,
                        forceQuad: true
                    );
                    if (withFrog)
                        renderer.Canvas.DrawSprite(frog, Vector2.Zero, Color.White, Vector2.One, 0);
                    renderer.Canvas.DrawSprite(
                        Sprite($"dust_circle_v2_{sourceFrames[frame]:00}_F"),
                        Vector2.Zero,
                        Color.White,
                        new(.8f),
                        rotation,
                        forceQuad: true
                    );
                    End();
                    device.SetRenderTarget(null);
                    reference.GetData(expected);
                    int differences = actual.Where((pixel, index) => pixel != expected[index]).Count();
                    // Mesh and quad rasterization can disagree at a few diagonal texel boundaries.
                    int allowedDifferences = rotation == 0 || rotation == MathF.PI / 2 ? 0 : 4;
                    if (differences > allowedDifferences && directory != null)
                    {
                        SaveFrame(Path.Combine(directory, "actual.png"));
                        using var stream = File.Create(Path.Combine(directory, "expected.png"));
                        reference.SaveAsPng(stream, Width, Height);
                    }
                    Check(
                        differences <= allowedDifferences,
                        $"smoke ring frame {frame} at {rotation}, frog={withFrog}: both source halves render completely in depth order"
                    );
                    if (directory != null && frame == 0)
                        SaveFrame(Path.Combine(directory, $"ring-{rotation:F2}-frog-{withFrog}.png"));
                }
            }
            renderer.Effects.Update(.051f, .36f);
            Check(!renderer.Effects.Active.Any(), $"smoke ring at {rotation} removes both halves after animation");
        }
        renderer.Reset();
        return checks.ToArray();
    }
}
