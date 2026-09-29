using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyFontEdges(string? directory = null)
    {
        const int width = 512;
        const int height = 256;
        using var target = new RenderTarget2D(
            device,
            width,
            height,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            4,
            RenderTargetUsage.DiscardContents
        );
        var pixels = new Color[width * height];
        var checks = new List<string>();
        var measurements = new Dictionary<string, object>();
        float selectedWidth = TextEdgeWidth;
        var scoreFont = assets.ScoreFont;
        var sideFont = assets.SideScoreFont;
        void Check(bool ok, string name)
        {
            if (!ok)
            {
                throw new InvalidOperationException(name);
            }

            checks.Add(name);
        }

        void Render(Action draw, string? name = null)
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
        }

        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        assets
            .UiEffect.Parameters["MatrixTransform"]
            .SetValue(Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1));
        try
        {
            foreach (var (edge, name) in new[] { (0f, "off"), (.5f, "narrow"), (1f, "normal") })
            {
                TextEdgeWidth = edge;
                foreach (
                    var (font, fontName, glyph, logical, baseline, top, bottom) in new[]
                    {
                        (scoreFont, "arcade H", "H", 20f, .81f, .563f, .065f),
                        (assets.Font, "retroville H", "H", 16f, 1f, .7f, 0f),
                        (assets.Font, "retroville dash", "-", 16f, 1f, .4f, .3f),
                        (assets.Font, "retroville a", "a", 16f, 1f, .5f, 0f),
                    }
                )
                {
                    int outside = 0;
                    int partial = 0;
                    float worst = 0;
                    foreach (float em in new[] { 12.8f, 16, 19.2f, 20, 24, 30, 33.333f, 48 })
                    {
                        foreach (float phase in new[] { 0, .125f, .25f, .5f, .875f })
                        {
                            var position = new Vector2(32 + phase, 32 + phase);
                            Render(() => font.Draw(batch, glyph, position, Color.White, em / logical));
                            float minY = position.Y + (baseline - top) * em;
                            float maxY = position.Y + (baseline - bottom) * em;
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int ink = pixels[y * width + x].R;
                                    if (ink == 0)
                                    {
                                        continue;
                                    }

                                    if (ink < 255)
                                    {
                                        partial++;
                                    }

                                    float distance = Math.Max(minY - (y + .5f), (y + .5f) - maxY);
                                    worst = Math.Max(worst, distance);

                                    if (distance > edge * .5f + .0625f)
                                    {
                                        outside++;
                                    }
                                }
                            }
                        }
                    }

                    measurements[fontName + "-" + name] = new
                    {
                        OutsidePixels = outside,
                        PartialPixels = partial,
                        WorstOutlineOvershootPixels = worst,
                    };
                    Check(outside == 0, $"{fontName} {name} has no top/bottom fringe outside outline coverage");
                    if (edge == 0)
                    {
                        Check(partial == 0, $"{fontName} hard text has no antialiased pixels across fractional sizes");
                    }
                }

                if (directory != null)
                {
                    for (int i = 0; i < 24; i++)
                    {
                        float phase = i / 24f;
                        Render(
                            () =>
                            {
                                assets.Font.Draw(batch, name.ToUpperInvariant(), new(16, 8), Color.White, 1.5f);
                                assets.Font.Draw(
                                    batch,
                                    "FROG SMASHERS + 123",
                                    new(16 + phase, 48 + phase),
                                    Color.White,
                                    1.5f
                                );
                                scoreFont.Draw(batch, "+1 0123456789", new(16 + phase, 98 + phase), Color.White, 1.5f);
                                assets.Font.Draw(batch, "ROUND 1 OF 5", new(16, 146), Color.White, 1.5f + phase * .25f);
                                sideFont.DrawCenteredVertical(
                                    batch,
                                    "+3 WIN!",
                                    new(130 + phase, 222 + phase),
                                    Color.White,
                                    1.5f,
                                    true
                                );
                            },
                            $"{name}-{i:00}"
                        );
                    }
                }
            }

            if (directory != null)
            {
                File.WriteAllText(
                    Path.Combine(directory, "edge-metrics.json"),
                    JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }) + "\n"
                );
            }
        }
        finally
        {
            TextEdgeWidth = selectedWidth;
            device.SetRenderTarget(null);
            assets
                .UiEffect.Parameters["MatrixTransform"]
                .SetValue(Matrix.CreateOrthographicOffCenter(0, Width, Height, 0, 0, 1));
        }

        return checks.ToArray();
    }
}
