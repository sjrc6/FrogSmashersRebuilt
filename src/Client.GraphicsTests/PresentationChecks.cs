using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks(Renderer renderer)
{
    private const int Width = Renderer.Width;
    private const int Height = Renderer.Height;
    private GraphicsDevice device => renderer.Canvas.Device;
    private Assets assets => renderer.Assets;
    private SpriteBatch batch => renderer.Batch;
    private static Color[] PlayerColors => Renderer.PlayerColors;

    private void Begin(float mode) => renderer.Canvas.Begin(mode);

    private void End() => renderer.Canvas.End();

    private void BeginFont() => renderer.Canvas.BeginFont();

    private void SaveFrame(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        device.SetRenderTarget(null);
        using var stream = File.Create(path);
        renderer.Frame.SaveAsPng(stream, renderer.Frame.Width, renderer.Frame.Height);
    }
}
