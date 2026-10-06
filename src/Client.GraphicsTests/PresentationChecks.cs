using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks(Renderer renderer)
{
    private GraphicsDevice device => renderer.Canvas.Device;
    private Assets assets => renderer.Assets;

    private void SaveFrame(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        device.SetRenderTarget(null);
        using var stream = File.Create(path);
        renderer.Frame.SaveAsPng(stream, renderer.Frame.Width, renderer.Frame.Height);
    }
}
