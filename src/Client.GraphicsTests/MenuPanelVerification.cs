using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyMenuPanels(string? directory)
    {
        if (directory != null)
            Directory.CreateDirectory(directory);
        var device = renderer.Batch.GraphicsDevice;
        var texture = assets.Texture("UI/menu-panel");
        var source = new Color[texture.Width * texture.Height];
        texture.GetData(source);
        var panel = new NineSlicePanel(texture);
        var checks = new List<string>();
        var background = new Color(31, 62, 93);
        using var batch = new SpriteBatch(device);
        try
        {
            foreach (
                var size in new[]
                {
                    new Point(1280, 720),
                    new Point(1920, 1080),
                    new Point(1600, 900),
                    new Point(1365, 768),
                }
            )
            {
                using var target = new RenderTarget2D(
                    device,
                    size.X,
                    size.Y,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None,
                    4,
                    RenderTargetUsage.DiscardContents
                );
                var outputScale = new Vector2(size.X / 1280f, size.Y / 720f);
                int pixels = Math.Max(
                    1,
                    (int)MathF.Round(2 * Math.Min(outputScale.X, outputScale.Y), MidpointRounding.AwayFromZero)
                );
                foreach (var bounds in new[] { new Rectangle(87, 75, 146, 118), new Rectangle(87, 75, 40, 40) })
                {
                    device.SetRenderTarget(target);
                    device.Clear(background);
                    batch.Begin(
                        blendState: BlendState.NonPremultiplied,
                        samplerState: SamplerState.PointClamp,
                        transformMatrix: Matrix.CreateScale(outputScale.X, outputScale.Y, 1)
                    );
                    panel.Draw(batch, bounds);
                    batch.End();
                    device.SetRenderTarget(null);
                    var actual = new Color[size.X * size.Y];
                    target.GetData(actual);
                    int left = (int)MathF.Round(bounds.X * outputScale.X / pixels) * pixels;
                    int top = (int)MathF.Round(bounds.Y * outputScale.Y / pixels) * pixels;
                    int tileWidth = texture.Width / 3;
                    int tileHeight = texture.Height / 3;
                    int width = Math.Max(tileWidth * 2, (int)MathF.Round(bounds.Width * outputScale.X / pixels));
                    int height = Math.Max(tileHeight * 2, (int)MathF.Round(bounds.Height * outputScale.Y / pixels));
                    for (int y = 0; y < size.Y; y++)
                    for (int x = 0; x < size.X; x++)
                    {
                        var expected = background;
                        if (x >= left && x < left + width * pixels && y >= top && y < top + height * pixels)
                        {
                            int nativeX = (x - left) / pixels;
                            int nativeY = (y - top) / pixels;
                            int SourceCoordinate(int coordinate, int length, int tile) =>
                                coordinate < tile ? coordinate
                                : coordinate >= length - tile ? 3 * tile - (length - coordinate)
                                : tile + (coordinate - tile) % tile;
                            int sx = SourceCoordinate(nativeX, width, tileWidth);
                            int sy = SourceCoordinate(nativeY, height, tileHeight);
                            var color = source[sy * texture.Width + sx];
                            if (color.A != 0)
                                expected = color;
                        }
                        if (actual[y * size.X + x] != expected)
                            throw new InvalidOperationException(
                                $"Panel at {size}, {bounds}: pixel {x},{y} was {actual[y * size.X + x]}, expected {expected}"
                            );
                    }
                    checks.Add(
                        $"Panel {width}x{height} at {size.X}x{size.Y} preserves corners, repeated tiles, partial tiles and transparency in {pixels}px blocks"
                    );
                    if (directory != null)
                    {
                        using var stream = File.Create(Path.Combine(directory, $"panel-{size.Y}p-{bounds.Width}.png"));
                        target.SaveAsPng(stream, size.X, size.Y);
                    }
                }
            }
        }
        finally
        {
            device.SetRenderTarget(null);
        }
        return checks.ToArray();
    }
}
