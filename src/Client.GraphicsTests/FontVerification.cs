using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyFontPresentation(string? directory = null)
    {
        const int width = 512;
        const int height = 256;
        using var target = new RenderTarget2D(device, width, height);
        var pixels = new Color[width * height];
        var checks = new List<string>();
        var metrics = new Dictionary<string, double>();
        float selectedEdgeWidth = TextEdgeWidth;
        TextEdgeWidth = 1;
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new InvalidOperationException(name);
            }

            checks.Add(name);
        }

        assets
            .UiEffect.Parameters["MatrixTransform"]
            .SetValue(Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1));
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        Color[] Render(Action draw, string? name = null)
        {
            device.SetRenderTarget(target);
            device.Clear(Color.Black);
            BeginFont();
            draw();
            batch.End();
            device.SetRenderTarget(null);
            target.GetData(pixels);
            if (directory != null && name != null)
            {
                using var stream = File.Create(Path.Combine(directory, name + ".png"));
                target.SaveAsPng(stream, width, height);
            }

            return pixels;
        }

        double Ink() => pixels.Sum(c => (double)c.R / 255);
        try
        {
            Check(
                MathF.Abs(assets.ScoreFont.Measure("0123").X - 44) < .00001,
                "distance text retains captured score advances"
            );
            foreach (
                var (font, name, advance) in new[]
                {
                    (assets.ScoreFont, "HUD", 58 * .2f),
                    (assets.OverheadFont, "overhead", 23 * .5f),
                    (assets.SideScoreFont, "KO edge", 47 * .25f),
                }
            )
            {
                Check(
                    Math.Abs(font.Measure("+1").X - font.Measure("1").X - advance) < .0001,
                    $"{name} signed award retains native fallback plus advance"
                );
                Render(() => font.Draw(batch, "+", new(32, 48), Color.White, 1.5f), $"{name.Replace(' ', '-')}-plus");
                Check(Ink() > 10, $"{name} fallback plus produces visible GPU pixels");
            }

            var translation = new List<double>();
            var scaling = new List<double>();
            foreach (var (font, name) in new[] { (assets.ScoreFont, "arcade"), (assets.Font, "retroville") })
            {
                translation.Clear();
                scaling.Clear();
                for (int i = 0; i < 16; i++)
                {
                    float phase = i / 16f;
                    Render(
                        () => font.Draw(batch, "0123456789", new(32 + phase, 48 + phase), Color.White, 1.5f),
                        $"{name}-phase-{i:00}"
                    );
                    translation.Add(Ink());
                    float scale = 1.35f + i * .02f;
                    Render(
                        () => font.Draw(batch, "0123456789", new(32 + phase, 48 + phase), Color.White, scale),
                        $"{name}-scale-{i:00}"
                    );
                    scaling.Add(Ink() / (scale * scale));
                }

                double phaseRange = (translation.Max() - translation.Min()) / translation.Average();
                double scaleRange = (scaling.Max() - scaling.Min()) / scaling.Average();
                metrics[name + "TranslationInkRange"] = phaseRange;
                metrics[name + "ScaleNormalizedInkRange"] = scaleRange;
                Check(phaseRange < .025, $"{name} subpixel translation preserves total ink within2.5%");
                Check(scaleRange < .05, $"{name} fractional scaling preserves normalized ink within5%");
            }

            Render(
                () =>
                {
                    batch.Draw(assets.White, new Rectangle(8, 8, 24, 24), Color.Red);
                    assets.ScoreFont.Draw(batch, "8", new(100, 40), Color.White, 3);
                    batch.Draw(assets.White, new Rectangle(8, 8, 24, 24), new Color(0, 0, 255, 128));
                },
                "mixed-ui"
            );
            var mixed = pixels[16 * width + 16];
            Check(
                Math.Abs(mixed.R - 127) <= 1 && mixed.G == 0 && Math.Abs(mixed.B - 128) <= 1,
                "mixed UI images keep straight-alpha blending and draw order"
            );
            Check(
                pixels.Any(c => c.R == 255 && c.G == 255 && c.B == 255),
                "glyph depth marker survives deferred UI batching"
            );
            Render(() => assets.ScoreFont.Draw(batch, "8", new(50, 30), new Color(80, 160, 240, 128), 4));
            var peak = pixels.MaxBy(c => c.B);
            Check(
                Math.Abs(peak.R - 40) <= 1 && Math.Abs(peak.G - 80) <= 1 && Math.Abs(peak.B - 120) <= 1,
                "distance text applies tint opacity once"
            );

            using (var field = new Texture2D(device, 1024, 1024))
            {
                var data = new Color[1024 * 1024];
                for (int y = 0; y < 1024; y++)
                {
                    for (int x = 0; x < 1024; x++)
                    {
                        float d = Math.Clamp(.5f + (x + .5f - 512) / 8f, 0, 1);
                        data[y * 1024 + x] = new Color(d, d, d, 1);
                    }
                }

                field.SetData(data);
                Render(
                    () =>
                        batch.Draw(
                            field,
                            new Vector2(20.3f, 20),
                            null,
                            Color.White,
                            0,
                            Vector2.Zero,
                            .25f,
                            SpriteEffects.None,
                            8f / 1024
                        ),
                    "analytic-edge"
                );
                var row = Enumerable.Range(50, 220).Select(x => pixels[100 * width + x].R).ToArray();
                Check(
                    row.Count(x => x > 0 && x < 255) <= 2,
                    "distance edge antialias transition is at most one pixel plus UNORM error"
                );
                Check(row.Zip(row.Skip(1)).All(p => p.First <= p.Second), "distance edge coverage is monotonic");
            }

            if (directory != null)
            {
                for (int i = 0; i < 60; i++)
                {
                    float t = i / 60f;
                    Render(
                        () =>
                        {
                            assets.Font.Draw(batch, "SMOOTH MOTION / SCALE", new(16, 10), Color.White, 1.5f);
                            assets.ScoreFont.Draw(
                                batch,
                                "0123456789",
                                new(32 + t * 6, 60 + t * 3),
                                PlayerColors[2],
                                1.5f
                            );
                            assets.Font.Draw(batch, "ROUND 1 OF 5", new(32, 110), Color.White, 1.5f + t * .3f);
                            assets.OverheadFont.DrawCenteredVertical(
                                batch,
                                "WIN!",
                                new(130 + t * 6, 190 + t * 3),
                                PlayerColors[6],
                                1.5f,
                                true
                            );
                        },
                        $"motion-{i:00}"
                    );
                }

                File.WriteAllText(
                    Path.Combine(directory, "metrics.json"),
                    JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }) + "\n"
                );
            }
        }
        finally
        {
            TextEdgeWidth = selectedEdgeWidth;
            device.SetRenderTarget(null);
            assets
                .UiEffect.Parameters["MatrixTransform"]
                .SetValue(Matrix.CreateOrthographicOffCenter(0, Width, Height, 0, 0, 1));
        }

        return checks.ToArray();
    }
}
