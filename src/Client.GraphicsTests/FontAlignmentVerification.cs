using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyFontAlignment(string? directory = null)
    {
        const int width = 512;
        const int height = 256;
        using var target = new RenderTarget2D(device, width, height);
        var pixels = new Color[width * height];
        var checks = new List<string>();
        float previousEdgeWidth = TextEdgeWidth;
        TextEdgeWidth = 1;
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }
        assets
            .UiEffect.Parameters["MatrixTransform"]
            .SetValue(Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1));
        try
        {
            foreach (
                var (font, name, rasterSize, upperBaseline, centerBaseline, inkTop, inkBottom) in new[]
                {
                    (assets.ScoreFont, "score", 100, 81, 31, -58, -9),
                    (assets.OverheadFont, "overhead", 40, 32, 12, -23, -4),
                    (assets.SideScoreFont, "side-score", 80, 65, 25, -46, -7),
                }
            )
            {
                foreach (bool centered in new[] { false, true })
                {
                    int originY = centered ? 128 : 40;
                    float scale = rasterSize / 20f;
                    device.SetRenderTarget(target);
                    device.Clear(Color.Black);
                    BeginFont();
                    if (centered)
                    {
                        font.DrawCenteredVertical(batch, "+", new(80, originY), Color.White, scale);
                    }
                    else
                    {
                        font.Draw(batch, "+", new(80, originY), Color.White, scale);
                    }
                    batch.End();
                    device.SetRenderTarget(null);
                    target.GetData(pixels);
                    int top = height;
                    int bottom = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            if (pixels[y * width + x].R >= 128)
                            {
                                top = Math.Min(top, y);
                                bottom = Math.Max(bottom, y + 1);
                            }
                        }
                    }
                    int baseline = originY + (centered ? centerBaseline : upperBaseline);
                    if (Math.Abs(top - baseline - inkTop) > 1 || Math.Abs(bottom - baseline - inkBottom) > 1)
                    {
                        throw new InvalidOperationException(
                            $"{name} plus bounds [{top}, {bottom}) differ from expected [{baseline + inkTop}, {baseline + inkBottom})"
                        );
                    }
                    checks.Add(
                        $"{name} plus {(centered ? "middle" : "upper")} baseline and ink bounds match within one pixel"
                    );
                    if (directory != null)
                    {
                        using var stream = File.Create(
                            Path.Combine(directory, $"{name}-{(centered ? "middle" : "upper")}.png")
                        );
                        target.SaveAsPng(stream, width, height);
                    }
                }
            }
        }
        finally
        {
            TextEdgeWidth = previousEdgeWidth;
            device.SetRenderTarget(null);
            assets
                .UiEffect.Parameters["MatrixTransform"]
                .SetValue(Matrix.CreateOrthographicOffCenter(0, Width, Height, 0, 0, 1));
        }
        return checks.ToArray();
    }
}
