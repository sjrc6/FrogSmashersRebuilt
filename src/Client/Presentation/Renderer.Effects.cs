using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed partial class Renderer
{
    private float confettiCounter;

    private float RandomRange(float low, float high) => MathHelper.Lerp(low, high, (float)cosmetics.NextDouble());

    private void SpawnPuff(MapData map, Vector2 feet, Color color, long tick, float age = 0)
    {
        float ground = feet.Y;
        float nearest = 1;
        foreach (var platform in map.Collision)
        {
            float left = (float)(platform.X - platform.Width / 2);
            float right = (float)(platform.X + platform.Width / 2);
            float top = (float)(platform.Y + platform.Height / 2);
            float distance = feet.Y - top;
            if (feet.X < left || feet.X > right || distance < -.001f || distance > nearest)
                continue;
            ground = top;
            nearest = distance;
        }

        // The opening frames' visible base is 14 pixels below the pivot, at 10 pixels per unit.
        effects.Add("SpawnPuff", new(feet.X, ground + 1.4f), color, tick, new EffectSpawn { Age = age });
    }

    private void DrawEffect(EffectSystem.VisualEffect e)
    {
        int mode = e.Owner >= 0 ? effects.ModeFor(assets.Data.Effects["FaderTrail"]) : effects.ModeFor(e.Data!);
        canvas.Begin(mode);
        canvas.DrawSprite(
            e.Sprite,
            e.Position + (e.CameraRelative ? cameraController.Position : Vector2.Zero),
            e.Color,
            e.CurrentScale,
            e.Rotation
        );
        canvas.End();
        if (e.Kind == EffectSystem.VisualEffectKind.ScorePlume)
        {
            var location = Screen(
                e.Position
                    + (e.CameraRelative ? cameraController.Position : Vector2.Zero)
                    + new Vector2(MathF.Cos(e.Rotation), MathF.Sin(e.Rotation)) * e.TextDistance
            );
            float scale = 2 * cameraController.PixelsPerUnit / 20 * e.TextScale;
            canvas.BeginFont();
            assets.ScoreFont.DrawCenteredVertical(Batch, e.Text, location, e.TextColor, scale, true);
            Batch.End();
        }
    }

    private void UpdateConfetti(World world, float dt)
    {
        float finishAge = (world.Rules.RoundFinishTicks - world.Match.PhaseTicks) / (float)World.TickRate;
        if (
            world.Match.Phase != MatchPhase.RoundFinished
            || finishAge < 1.5f
            || world.Match.Winner < 0
            || !world.Players[world.Match.Winner].Alive
        )
        {
            return;
        }
        float extent = cameraController.HalfHeight * Width / Height;
        confettiCounter -= dt;
        if (confettiCounter < 0)
        {
            confettiCounter = .03f;
            float y = cameraController.Position.Y + cameraController.HalfHeight * RandomRange(.75f, 1.5f);
            float x = RandomRange(
                cameraController.Position.X - extent * RandomRange(.5f, 1.5f),
                cameraController.Position.X + extent
            );
            var colors = assets.Data.EffectsColors;
            var color = colors.Length > 0 ? EffectSystem.ToColor(colors[cosmetics.Next(colors.Length)]) : Color.White;
            effects.Add(
                "Confetti.",
                new(x, y),
                color,
                world.TickNumber,
                new EffectSpawn { Velocity = new(RandomRange(-1, 1), RandomRange(-.5f, -2)) }
            );
        }
    }

    private void DrawTongue(Vector2 origin, Vector2 tip, CharacterPresentation animator)
    {
        var texture = assets.Texture(assets.Data.Materials[assets.Data.TongueLineMaterial].Textures["_MainTex"]);
        var a = Screen(origin);
        var b = Screen(tip);
        float angle = MathF.Atan2(b.Y - a.Y, b.X - a.X);

        canvas.Begin(0);
        Batch.Draw(
            texture,
            a,
            null,
            Color.White,
            angle,
            new Vector2(0, texture.Height * .5f),
            new Vector2(
                Vector2.Distance(a, b) / texture.Width,
                assets.Data.TongueLineWidth * cameraController.PixelsPerUnit / texture.Height
            ),
            SpriteEffects.None,
            0
        );
        canvas.End();
        var data = assets.Data.TongueTip;
        var sprite =
            animator.TipClock.Frame < 0
                ? data.InitialSpriteId
                : data.Frames[animator.TipClock.Frame % data.Frames.Length];
        canvas.Begin(effects.ModeFor(data));
        canvas.DrawSprite(
            sprite,
            tip,
            EffectSystem.ToColor(data.Color),
            new(data.ScaleX, data.ScaleY),
            MathF.Atan2(tip.Y - origin.Y, tip.X - origin.X)
        );
        canvas.End();
    }
}
