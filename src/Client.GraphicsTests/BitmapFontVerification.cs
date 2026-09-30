using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyBitmapFonts(string? directory = null)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }

        if (directory != null)
            Directory.CreateDirectory(directory);
        try
        {
            foreach (var (name, font) in new[] { ("retroville", assets.Font) })
            {
                var definition = FontDefinition.Load(Path.Combine(assets.Root, "UI", name + ".json"));
                var texture = assets.Texture(definition.Texture);
                var atlas = new Color[texture.Width * texture.Height];
                texture.GetData(atlas);
                Check(
                    atlas.All(pixel => pixel.A == 0 || pixel == Color.White),
                    name + " atlas contains only transparent or solid white pixels"
                );
                Check(
                    Enumerable.Range(32, 95).All(code => definition.Glyphs.ContainsKey(code))
                        && Enumerable.Range(33, 94).All(code => definition.Glyphs[code].Region.Length == 4),
                    name + " contains printable ASCII, including punctuation"
                );

                int Ink(char character)
                {
                    var region = definition.Glyphs[character].Region;
                    if (region.Length == 0)
                        return 0;
                    int count = 0;
                    for (int y = region[1]; y < region[1] + region[3]; y++)
                    for (int x = region[0]; x < region[0] + region[2]; x++)
                        if (atlas[y * texture.Width + x].A == 255)
                            count++;
                    return count;
                }

                const string sample = "H +1 008 Qq";
                int nativeInk = sample.Sum(Ink);
                foreach (
                    var (width, height, pixelSize) in new[]
                    {
                        (1280, 720, 2),
                        (1920, 1080, 3),
                        (1600, 900, 3),
                        (1365, 768, 2),
                    }
                )
                {
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

                    void Render(Action draw)
                    {
                        device.SetRenderTarget(target);
                        device.Clear(Color.Black);
                        BeginFont();
                        draw();
                        End();
                        device.SetRenderTarget(null);
                        target.GetData(pixels);
                    }

                    bool crisp = true;
                    bool uniform = true;
                    bool complete = true;
                    foreach (float phase in new[] { 0, .2f, .5f, .9f })
                    {
                        Render(() =>
                            font.DrawCenteredVertical(
                                batch,
                                sample,
                                new(640 + phase, 100 + phase),
                                Color.White,
                                center: true
                            )
                        );
                        crisp &= pixels.All(pixel => pixel == Color.Black || pixel == Color.White);
                        complete &= pixels.Count(pixel => pixel == Color.White) == nativeInk * pixelSize * pixelSize;
                        for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int origin = (y / pixelSize * pixelSize) * width + x / pixelSize * pixelSize;
                            uniform &= pixels[y * width + x] == pixels[origin];
                        }
                    }
                    Check(crisp, $"{name} at {height}p has no blended font edges on a multisampled target");
                    Check(
                        uniform,
                        $"{name} at {height}p uses uniform {pixelSize}x{pixelSize} blocks at fractional positions"
                    );
                    Check(complete, $"{name} at {height}p preserves every source glyph pixel");

                    Render(() => font.DrawCenteredVertical(batch, "H", new(640, 200), Color.White, center: true));
                    var bounds = InkBounds(pixels, width, height);
                    Check(
                        Math.Abs((bounds.Top + bounds.Bottom) / 2f - 200 * height / 720f) <= pixelSize / 2f
                            && bounds.Height == 7 * pixelSize,
                        $"{name} at {height}p centers visible capitals on the requested line"
                    );

                    Render(() =>
                    {
                        batch.Draw(assets.White, new Rectangle(8, 8, 24, 24), Color.Red);
                        font.Draw(batch, "+8", new(80, 40), new Color(80, 160, 240, 128));
                        batch.Draw(assets.White, new Rectangle(8, 8, 24, 24), new Color(0, 0, 255, 128));
                    });
                    var tint = pixels.First(pixel => pixel.G > 0);
                    Check(
                        Math.Abs(tint.R - 40) <= 1 && Math.Abs(tint.G - 80) <= 1 && Math.Abs(tint.B - 120) <= 1,
                        $"{name} at {height}p applies text color and transparency once"
                    );
                    int sampleX = 16 * width / 1280;
                    int sampleY = 16 * height / 720;
                    var mixed = pixels[sampleY * width + sampleX];
                    Check(
                        Math.Abs(mixed.R - 127) <= 1 && mixed.G == 0 && Math.Abs(mixed.B - 128) <= 1,
                        $"{name} at {height}p preserves image blending and draw order in the UI batch"
                    );

                    if (directory != null)
                    {
                        Render(() =>
                        {
                            font.Draw(batch, "ABCDEFGHIJKLMNOPQRSTUVWXYZ", new(48, 48), Color.White);
                            font.Draw(batch, "abcdefghijklmnopqrstuvwxyz", new(48, 80), Color.White);
                            font.Draw(batch, "0123456789 +-*/!?.,:;[](){}", new(48, 112), Color.White);
                            font.Draw(batch, "LOCAL LOBBY / WINNER! / +3", new(48, 160), PlayerColors[2]);
                            font.DrawCenteredVertical(batch, "CENTERED HINT", new(640, 240), Color.White, center: true);
                            font.Draw(batch, font.Wrap("BORDERLESS FULLSCREEN: OFF", 280), new(48, 300), Color.White);
                            font.Draw(batch, "LARGE TEXT", new(48, 400), Color.White, 2);
                        });
                        using var stream = File.Create(Path.Combine(directory, $"{name}-{height}p.png"));
                        target.SaveAsPng(stream, width, height);
                    }
                }

                string wrapped = font.Wrap("FIRST ARENA: FINALFROGSTINATION", 280);
                Check(
                    wrapped.Contains('\n') && wrapped.Split('\n').All(line => font.Measure(line).X <= 280),
                    name + " wraps long labels without shrinking glyphs"
                );
                string clipped = font.Wrap(new string('W', 128), 280, 2);
                Check(
                    clipped.Split('\n').Length == 2
                        && clipped.EndsWith("...")
                        && clipped.Split('\n').All(line => font.Measure(line).X <= 280),
                    name + " keeps long unbroken input within two menu lines"
                );
                Check(font.Measure("+1").X > font.Measure("1").X, name + " signed awards include the plus advance");
            }
        }
        finally
        {
            device.SetRenderTarget(null);
        }
        checks.AddRange(VerifyScoreFont(directory));
        return checks.ToArray();
    }

    private static Rectangle InkBounds(Color[] pixels, int width, int height)
    {
        int left = width,
            top = height,
            right = 0,
            bottom = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (pixels[y * width + x] == Color.White)
            {
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        return new Rectangle(left, top, right - left, bottom - top);
    }
}
