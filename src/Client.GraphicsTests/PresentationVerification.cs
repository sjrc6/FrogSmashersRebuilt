using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyPresentation()
    {
        var checks = new List<string>();
        void True(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name);
            }

            checks.Add(name);
        }

        True(EffectAnimation.Powered(true, false, true), "fly power remains after stale Burping phase");
        True(!EffectAnimation.Powered(true, true, true), "fly power hidden during burp animation");
        var world = new World(assets.Data, new GameRules(playerCount: 2));
        var p = world.Players[0];
        p.Alive = true;
        p.Mode = CharacterMode.Normal;
        p.HasFly = true;
        p.TonguePhase = TonguePhase.Burping;
        var animator = new CharacterPresentation { Color = renderer.ColorFor(world, 0) };
        renderer.Effects.UpdateCharacter(p, world, Vector2.Zero, animator, .07f);
        True(animator.Color == Color.Black, "powered color sequence begins black");
        renderer.Effects.UpdateCharacter(p, world, Vector2.Zero, animator, .07f);
        True(animator.Color == renderer.ColorFor(world, 0), "powered color sequence returns player color");
        renderer.Effects.UpdateCharacter(p, world, Vector2.Zero, animator, .07f);
        True(animator.Color == Color.White, "powered color sequence reaches white");
        var pose = new CharacterPresentation();
        p.OnGround = true;
        p.Mode = CharacterMode.Tongue;
        p.TonguePhase = TonguePhase.Burping;
        pose.Update(assets, p, world, 0);
        True(pose.Sprite == assets.Frame("tongueBurp", 0), "burp starts at authored frame zero");
        pose.Update(assets, p, world, .051f);
        True(pose.Sprite == assets.Frame("tongueBurp", 1), "burp advances after 50ms delay");
        p.Mode = CharacterMode.Attacking;
        p.AttackPhase = AttackPhase.Swing;
        p.AttackX = 0;
        p.AttackY = -1;
        pose.Update(assets, p, world, 0);
        True(pose.Sprite == assets.Frame("attackDown", 0), "directional attack resets clip frame");
        pose.Update(assets, p, world, .051f);
        True(pose.Sprite == assets.Frame("attackDown", 1, true), "directional attack uses 50ms frame delay");
        p.Mode = CharacterMode.Normal;
        p.OnGround = false;
        p.WallSliding = true;
        pose.Update(assets, p, world, 0);
        p.WallSliding = false;
        p.VY = 10;
        pose.Update(assets, p, world, 0);
        True(
            pose.Transitioning && pose.Sprite == assets.Frame("wallSlideJumpLaunch", 0),
            "wall jump preserves authored transition sprite"
        );
        using var target = new RenderTarget2D(device, Width, Height);
        using var tex = new Texture2D(device, 1, 1);
        var pixels = new Color[Width * Height];
        void Pixel(float mode, Color textureColor, Color vertex, Color expected, string name)
        {
            tex.SetData([textureColor]);
            device.SetRenderTarget(target);
            device.Clear(new Color(100, 100, 100));
            Begin(mode);
            batch.Draw(tex, new Rectangle(0, 0, 16, 16), vertex);
            End();
            device.SetRenderTarget(null);
            target.GetData(pixels);
            var value = pixels[8 * Width + 8];
            if (
                Math.Abs(value.R - expected.R) > 2
                || Math.Abs(value.G - expected.G) > 2
                || Math.Abs(value.B - expected.B) > 2
            )
            {
                throw new InvalidOperationException($"{name}: {value} != {expected}");
            }

            checks.Add(name);
        }

        Pixel(
            3,
            Color.White,
            new(200, 100, 50, 128),
            new(150, 100, 75),
            "actual FaderTrail ColorCutout blends opacity once"
        );
        Pixel(
            1,
            new(170, 176, 88, 255),
            new(10, 20, 30, 0),
            new(100, 100, 100),
            "transparent palette sprites leave the background visible"
        );
        Pixel(
            1,
            new(170, 176, 88, 255),
            new(20, 100, 200, 90),
            new(72, 100, 135),
            "translucent lobby frogs blend their selected color once"
        );
        Pixel(
            1,
            new(125, 99, 75, 255),
            new(20, 100, 200, 90),
            new(68, 82, 100),
            "translucent lobby frogs retain palette shadows"
        );
        Pixel(
            1,
            Color.Black,
            new(20, 100, 200, 90),
            new(65, 65, 65),
            "translucent lobby frog outlines share the body opacity"
        );
        Pixel(
            1,
            new(125, 99, 75, 255),
            new(10, 20, 30, 255),
            new(5, 10, 15),
            "authored shadow maps to half the player color"
        );
        for (int slot = 0; slot < PlayerColors.Length; slot++)
        {
            var tint = PlayerColors[slot];
            Pixel(1, new(170, 176, 88, 255), tint, tint, $"player{slot + 1} primary palette replacement");
            Pixel(
                1,
                new(125, 99, 75, 255),
                tint,
                new(tint.R / 2, tint.G / 2, tint.B / 2),
                $"player{slot + 1} authored half-color shadow"
            );
        }

        Pixel(
            1,
            new(124, 99, 75, 255),
            Color.Red,
            new(124, 99, 75),
            "palette preserves neighboring nonmatching red124"
        );
        Pixel(
            1,
            new(126, 99, 75, 255),
            Color.Red,
            new(126, 99, 75),
            "palette preserves neighboring nonmatching red126"
        );
        Pixel(1, new(170, 11, 12, 255), new(20, 50, 90), new(20, 50, 90), "palette condition compares red only");
        Pixel(
            1,
            new(170, 176, 88, 255),
            new(125, 200, 80),
            new(63, 100, 40),
            "palette second comparison sees first replacement"
        );
        Pixel(1, Color.Black, Color.Magenta, Color.Black, "palette preserves black feature outlines");
        Pixel(1, new(255, 235, 99), Color.Magenta, new(255, 235, 99), "palette preserves yellow eye highlights");
        Pixel(1, Color.White, Color.Magenta, Color.White, "palette preserves white highlights");
        Pixel(1, new(170, 176, 88, 127), Color.Red, new(100, 100, 100), "palette alpha cutoff rejects127");
        Pixel(
            0,
            Color.White,
            new(200, 100, 50, 128),
            new(150, 100, 75),
            "SpritesDefault premultiplies RGB before blend"
        );
        using (var gradient = new Texture2D(device, Width, Height))
        {
            using (var normal = new Texture2D(device, 1, 1))
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        pixels[y * Width + x] = new Color(x % 256, y % 256, 0);
                    }
                }

                gradient.SetData(pixels);
                normal.SetData([new Color(0, 128, 0, 255)]);
                var fx = assets.DistortionEffect;
                fx.Parameters["Resolution"].SetValue(new Vector2(Width, Height));
                fx.Parameters["Shimmer"].SetValue(0f);
                fx.Parameters["MaskTexture"].SetValue(assets.White);
                var points = new Vector4[8];
                points[0] = new(0, 0, 1, 1);
                var vectors = new Vector4[8];
                vectors[0] = new(8, 4, .5f, 0);
                fx.Parameters["ImpactRect"].SetValue(points[0]);
                fx.Parameters["ImpactOffset"].SetValue(vectors[0]);
                device.SetRenderTarget(target);
                batch.Begin(effect: fx, blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp);
                batch.Draw(gradient, new Rectangle(0, 0, Width, Height), Color.White);
                batch.End();
                device.SetRenderTarget(null);
                target.GetData(pixels);
                True(
                    pixels[100 * Width + 100] == new Color(104, 102, 0),
                    "localized GrabPass applies native-pixel offset and alpha mask"
                );
                fx.Parameters["ImpactRect"].SetValue(Vector4.Zero);
                fx.Parameters["ImpactOffset"].SetValue(Vector4.Zero);
                fx.Parameters["Shimmer"].SetValue(1f);
                fx.Parameters["ShimmerRect"].SetValue(new Vector4(0, 0, 1, 1));
                fx.Parameters["ShimmerParams"].SetValue(new Vector4(8, 0, 0, 0));
                fx.Parameters["NoiseTexture"].SetValue(normal);
                fx.Parameters["NoiseTransform"].SetValue(new Vector4(1, 1, 0, 0));
                fx.Parameters["PackedNormal"].SetValue(1f);
                device.SetRenderTarget(target);
                batch.Begin(effect: fx, blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp);
                batch.Draw(gradient, new Rectangle(0, 0, Width, Height), Color.White);
                batch.End();
                device.SetRenderTarget(null);
                target.GetData(pixels);
                True(
                    pixels[100 * Width + 100] == new Color(108, 100, 0),
                    "native DXT5nm alpha drives shimmer horizontal offset"
                );
                fx.Parameters["Shimmer"].SetValue(0f);
            }
        }

        checks.AddRange(VerifyBitmapFonts());
        checks.AddRange(VerifyScorePresentation());
        renderer.Reset();
        return checks.ToArray();
    }
}
