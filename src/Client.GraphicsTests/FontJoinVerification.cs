using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyFontJoins(string? directory = null)
    {
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        var checks = new List<string>();
        float selectedWidth = TextEdgeWidth;
        using var target = new RenderTarget2D(device, 128, 128);
        var pixels = new Color[128 * 128];
        DistanceFieldFont currentFont = assets.ScoreFont;
        float logicalSize = 20;

        int[][][] eight =
        [
            [
                [499, 137],
                [431, 137],
                [431, 65],
                [73, 65],
                [73, 133],
                [1, 133],
                [1, 278],
                [71, 278],
                [71, 348],
                [1, 348],
                [1, 492],
                [69, 492],
                [69, 563],
                [356, 563],
                [356, 495],
                [428, 495],
                [428, 351],
                [358, 351],
                [358, 281],
                [499, 281],
            ],
            [
                [284, 351],
                [284, 492],
                [144, 492],
                [144, 419],
                [216, 419],
                [216, 351],
            ],
            [
                [356, 137],
                [356, 210],
                [214, 210],
                [214, 278],
                [144, 278],
                [144, 137],
            ],
        ];
        int[][][] zero =
        [
            [
                [499, 208],
                [431, 208],
                [431, 137],
                [360, 137],
                [360, 65],
                [144, 65],
                [144, 133],
                [73, 133],
                [73, 205],
                [1, 205],
                [1, 421],
                [69, 421],
                [69, 492],
                [141, 492],
                [141, 563],
                [356, 563],
                [356, 495],
                [428, 495],
                [428, 424],
                [499, 424],
            ],
            [
                [284, 137],
                [284, 209],
                [356, 209],
                [356, 424],
                [285, 424],
                [285, 492],
                [216, 492],
                [216, 420],
                [144, 420],
                [144, 205],
                [216, 205],
                [216, 137],
            ],
        ];
        int[][][] cue =
        [
            [
                [499, 208],
                [499, 65],
                [430, 65],
                [430, 135],
                [360, 135],
                [360, 65],
                [73, 65],
                [73, 133],
                [1, 133],
                [1, 492],
                [69, 492],
                [69, 563],
                [428, 563],
                [428, 495],
                [499, 495],
            ],
            [
                [356, 280],
                [356, 492],
                [144, 492],
                [144, 137],
                [285, 137],
                [285, 210],
                [215, 210],
                [215, 280],
            ],
        ];
        int[][][] retroEight =
        [
            [
                [100, 400],
                [0, 400],
                [0, 600],
                [100, 600],
                [100, 700],
                [500, 700],
                [500, 600],
                [600, 600],
                [600, 400],
                [500, 400],
                [500, 300],
                [700, 300],
                [700, 100],
                [600, 100],
                [600, 0],
                [100, 0],
                [100, 100],
                [0, 100],
                [0, 300],
                [100, 300],
            ],
            [
                [400, 400],
                [400, 600],
                [200, 600],
                [200, 500],
                [300, 500],
                [300, 400],
            ],
            [
                [200, 300],
                [200, 100],
                [500, 100],
                [500, 200],
                [400, 200],
                [400, 300],
            ],
        ];
        bool Inside(int[][][] contours, double x, double y)
        {
            int winding = 0;
            foreach (var contour in contours)
            {
                for (int i = 0, j = contour.Length - 1; i < contour.Length; j = i++)
                {
                    var a = contour[i];
                    var b = contour[j];
                    double cross = (b[0] - a[0]) * (y - a[1]) - (x - a[0]) * (b[1] - a[1]);
                    if (a[1] <= y && b[1] > y && cross > 0)
                    {
                        winding++;
                    }
                    else if (a[1] > y && b[1] <= y && cross < 0)
                    {
                        winding--;
                    }
                }
            }

            return winding != 0;
        }

        int Components(bool[] mask, bool diagonal)
        {
            var seen = new bool[mask.Length];
            var pending = new Stack<int>();
            int count = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i] || seen[i])
                {
                    continue;
                }

                count++;
                seen[i] = true;
                pending.Push(i);
                while (pending.Count > 0)
                {
                    int point = pending.Pop();
                    int px = point % 128;
                    int py = point / 128;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0 || !diagonal && dx != 0 && dy != 0)
                            {
                                continue;
                            }

                            int x = px + dx;
                            int y = py + dy;
                            if (x < 0 || x >= 128 || y < 0 || y >= 128)
                            {
                                continue;
                            }

                            int next = y * 128 + x;
                            if (!mask[next] || seen[next])
                            {
                                continue;
                            }

                            seen[next] = true;
                            pending.Push(next);
                        }
                    }
                }
            }

            return count;
        }

        void Render(string text, Vector2 position, float em, string? filename = null)
        {
            device.SetRenderTarget(target);
            device.Clear(Color.Black);
            BeginFont();
            currentFont.Draw(batch, text, position, Color.White, em / logicalSize);
            batch.End();
            device.SetRenderTarget(null);
            target.GetData(pixels);
            if (directory != null && filename != null)
            {
                using var stream = File.Create(Path.Combine(directory, filename + ".png"));
                target.SaveAsPng(stream, 128, 128);
            }
        }

        try
        {
            TextEdgeWidth = 0;
            assets
                .UiEffect.Parameters["MatrixTransform"]
                .SetValue(Matrix.CreateOrthographicOffCenter(0, 128, 128, 0, 0, 1));
            foreach (
                var (glyph, contours, retro) in new[]
                {
                    ("0", zero, false),
                    ("8", eight, false),
                    ("Q", cue, false),
                    ("q", cue, false),
                    ("8", retroEight, true),
                }
            )
            {
                currentFont = retro ? assets.Font : assets.ScoreFont;
                logicalSize = retro ? 16 : 20;
                double baseline = retro ? 1 : .81;
                string label = (retro ? "Retroville " : "Arcade ") + glyph;
                int breaks = 0;
                int counterBreaks = 0;
                int cases = 0;
                foreach (float em in new[] { 20f, 30, 40, 60, 100 })
                {
                    foreach (float xPhase in new[] { 0f, .25f, .3125f, .4375f, .5f, .625f, .75f, .9375f })
                    {
                        foreach (float yPhase in new[] { 0f, .25f, .5f, .5625f, .625f, .75f })
                        {
                            cases++;
                            var position = new Vector2(16 + xPhase, 16 + yPhase);
                            Render(glyph, position, em);
                            var expected = new bool[pixels.Length];
                            var actual = new bool[pixels.Length];
                            for (int y = 0; y < 128; y++)
                            {
                                for (int x = 0; x < 128; x++)
                                {
                                    int index = y * 128 + x;
                                    actual[index] = pixels[index].R >= 128;
                                    expected[index] = Inside(
                                        contours,
                                        (x + .5 - position.X) / em * 1000,
                                        (position.Y + baseline * em - y - .5) / em * 1000
                                    );
                                }
                            }

                            foreach (bool diagonal in new[] { false, true })
                            {
                                if (Components(actual, diagonal) > Components(expected, diagonal))
                                {
                                    breaks++;
                                }
                            }

                            for (int i = 0; i < actual.Length; i++)
                            {
                                actual[i] = !actual[i];
                                expected[i] = !expected[i];
                            }

                            if (Components(actual, false) < Components(expected, false))
                            {
                                counterBreaks++;
                            }
                        }
                    }
                }

                if (breaks != 0)
                {
                    throw new InvalidDataException(
                        $"Font join regression: {label} gains {breaks} disconnected components"
                    );
                }

                checks.Add($"{label} keeps its intended stroke connections across {cases} sizes/positions");
                if (counterBreaks != 0)
                {
                    throw new InvalidDataException(
                        $"Font join regression: {label} opens {counterBreaks} enclosed counters"
                    );
                }

                checks.Add($"{label} keeps its enclosed counters across {cases} sizes/positions");
                var points =
                    retro
                        ? new[]
                        {
                            new Vector2(450, 250),
                            new Vector2(150, 150),
                            new Vector2(150, 250),
                            new Vector2(450, 450),
                            new Vector2(450, 550),
                        }
                    : glyph == "8"
                        ? new[]
                        {
                            new Vector2(100, 160),
                            new Vector2(100, 260),
                            new Vector2(320, 380),
                            new Vector2(320, 470),
                        }
                    : glyph == "0" ? new[] { new Vector2(180, 460), new Vector2(320, 170) }
                    : new[] { new Vector2(470, 170) };
                foreach (var point in points)
                {
                    const float em = 30;
                    var position = new Vector2(64.5f - point.X * .03f, 64.5f + point.Y * .03f - (float)baseline * em);
                    Render(glyph, position, em);
                    if (pixels[64 * 128 + 64].R != 255)
                    {
                        throw new InvalidDataException(
                            $"Font join regression: {label} reinforced point {point} is not filled"
                        );
                    }
                }

                checks.Add($"{label} fills its approved source-grid join blocks");
                Render(
                    glyph,
                    new(16.25f, 16.25f),
                    30,
                    retro ? "retroville-8"
                        : glyph == "q" ? "lowercase-q"
                        : "glyph-" + glyph
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
