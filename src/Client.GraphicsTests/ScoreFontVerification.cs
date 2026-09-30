using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    private string[] VerifyScoreFont(string? directory)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }

        var font = assets.ScoreFont;
        var definition = FontDefinition.Load(Path.Combine(assets.Root, "UI", "arcade.json"));
        var texture = assets.Texture(definition.Texture);
        var atlas = new Color[texture.Width * texture.Height];
        texture.GetData(atlas);
        Check(atlas.All(pixel => pixel.A == 0 || pixel == Color.White), "score glyphs have solid native pixels");
        Check(
            Enumerable.Range(32, 95).All(code => definition.Glyphs.ContainsKey(code))
                && Enumerable.Range(33, 94).All(code => definition.Glyphs[code].Region.Length == 4),
            "score atlas contains printable ASCII and punctuation"
        );

        string[] GlyphRows(char character)
        {
            var region = definition.Glyphs[character].Region;
            return Enumerable
                .Range(region[1], region[3])
                .Select(y => new string(
                    Enumerable
                        .Range(region[0], region[2])
                        .Select(x => atlas[y * texture.Width + x].A == 255 ? '#' : '.')
                        .ToArray()
                ))
                .ToArray();
        }

        Check(
            GlyphRows('1')
                .SequenceEqual(new[] { "..##..", ".###..", "..##..", "..##..", "..##..", "..##..", "######" }),
            "score 1 retains its two-pixel stem and six-pixel foot"
        );
        Check(
            GlyphRows('4')
                .SequenceEqual(new[] { "...###.", "..####.", ".##.##.", "##..##.", "#######", "....##.", "....##." }),
            "score 4 retains its stepped diagonal, counter and two-pixel stem"
        );
        foreach (var (character, count) in new[] { ('0', 1), ('4', 1), ('6', 1), ('8', 2), ('9', 1) })
            Check(GlyphCounters(GlyphRows(character)) == count, $"score {character} retains {count} enclosed counters");

        const string sample = "01489";
        var normal = font.Measure(sample);
        Check(Math.Abs(normal.Y - 10) < .0001f, "score capitals use the original ten-pixel display height");
        foreach (float scale in new[] { 0, .001f, .2f, .499f, .5f, .501f, .749f, .75f, .751f, 1.249f, 1.25f, 1.251f })
            Check(
                Vector2.Distance(font.Measure(sample, scale), normal * scale) < .0001f,
                $"score metrics scale continuously at {scale}"
            );

        try
        {
            foreach (var (width, height) in new[] { (1280, 720), (1920, 1080) })
            {
                using var target = new RenderTarget2D(
                    device,
                    width,
                    height,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None,
                    4,
                    RenderTargetUsage.PreserveContents
                );
                var pixels = new Color[width * height];
                var sizes = new HashSet<Point>();
                var previous = Rectangle.Empty;
                int largestWidthStep = 0,
                    largestHeightStep = 0;
                string? frames = directory == null ? null : Path.Combine(directory, $"scores-motion-{height}p");
                if (frames != null)
                    Directory.CreateDirectory(frames);

                for (int frame = 0; frame <= 60; frame++)
                {
                    float scale = .9f + frame / 100f;
                    device.SetRenderTarget(target);
                    device.Clear(Color.Black);
                    BeginFont();
                    font.DrawCenteredVertical(batch, sample, new(640, 160), Color.White, scale, true);
                    End();
                    device.SetRenderTarget(null);
                    target.GetData(pixels);
                    var bounds = InkBounds(pixels, width, height);
                    sizes.Add(bounds.Size);
                    if (frame > 0)
                    {
                        largestWidthStep = Math.Max(largestWidthStep, Math.Abs(bounds.Width - previous.Width));
                        largestHeightStep = Math.Max(largestHeightStep, Math.Abs(bounds.Height - previous.Height));
                    }
                    previous = bounds;

                    if (frames != null)
                    {
                        device.SetRenderTarget(target);
                        BeginFont();
                        font.Draw(batch, "0123456789", new(100, 60), Color.White, 1.4f);
                        font.DrawCenteredVertical(batch, "+4", new(640, 300), Color.White, 1.5f - frame / 120f, true);
                        font.DrawCenteredVertical(batch, "WIN! +8", new(640, 460), PlayerColors[2], scale * 2, true);
                        End();
                        device.SetRenderTarget(null);
                        using var stream = File.Create(Path.Combine(frames, $"frame-{frame:00}.png"));
                        target.SaveAsPng(stream, width, height);
                    }
                }
                Check(
                    largestWidthStep <= 2 && largestHeightStep <= 2 && sizes.Count >= 20,
                    $"scores at {height}p grow continuously ({sizes.Count} sizes; largest width/height steps {largestWidthStep}/{largestHeightStep} pixels)"
                );
            }
        }
        finally
        {
            device.SetRenderTarget(null);
        }
        return checks.ToArray();
    }

    private static int GlyphCounters(string[] rows)
    {
        var seen = new HashSet<Point>();
        int counters = 0;
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < rows[y].Length; x++)
        {
            var start = new Point(x, y);
            if (rows[y][x] != '.' || !seen.Add(start))
                continue;
            bool border = false;
            var queue = new Queue<Point>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var point))
            {
                border |= point.X == 0 || point.Y == 0 || point.X == rows[0].Length - 1 || point.Y == rows.Length - 1;
                foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
                {
                    var next = point + offset;
                    if (
                        next.X >= 0
                        && next.X < rows[0].Length
                        && next.Y >= 0
                        && next.Y < rows.Length
                        && rows[next.Y][next.X] == '.'
                        && seen.Add(next)
                    )
                        queue.Enqueue(next);
                }
            }
            if (!border)
                counters++;
        }
        return counters;
    }
}
