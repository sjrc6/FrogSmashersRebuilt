using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class NineSlicePanel
{
    private readonly Texture2D texture;
    private readonly int tileWidth;
    private readonly int tileHeight;

    public NineSlicePanel(Texture2D texture)
    {
        if (texture.Width % 3 != 0 || texture.Height % 3 != 0)
            throw new ArgumentException("Panel atlas must contain a 3 by 3 grid of equal tiles", nameof(texture));
        this.texture = texture;
        tileWidth = texture.Width / 3;
        tileHeight = texture.Height / 3;
    }

    public void Draw(SpriteBatch batch, Rectangle bounds)
    {
        var viewport = texture.GraphicsDevice.Viewport;
        var outputScale = new Vector2(viewport.Width / (float)Renderer.Width, viewport.Height / (float)Renderer.Height);
        float pixels = Math.Max(
            1,
            MathF.Round(2 * Math.Min(outputScale.X, outputScale.Y), MidpointRounding.AwayFromZero)
        );
        var scale = new Vector2(pixels) / outputScale;
        var origin = new Vector2(MathF.Round(bounds.X / scale.X), MathF.Round(bounds.Y / scale.Y)) * scale;
        int width = Math.Max(tileWidth * 2, (int)MathF.Round(bounds.Width / scale.X));
        int height = Math.Max(tileHeight * 2, (int)MathF.Round(bounds.Height / scale.Y));
        ReadOnlySpan<int> columns = [0, tileWidth, width - tileWidth, width];
        ReadOnlySpan<int> rows = [0, tileHeight, height - tileHeight, height];

        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        for (int y = rows[row]; y < rows[row + 1]; y += tileHeight)
        for (int x = columns[column]; x < columns[column + 1]; x += tileWidth)
        {
            var source = new Rectangle(
                column * tileWidth,
                row * tileHeight,
                Math.Min(tileWidth, columns[column + 1] - x),
                Math.Min(tileHeight, rows[row + 1] - y)
            );
            batch.Draw(
                texture,
                origin + new Vector2(x, y) * scale,
                source,
                Color.White,
                0,
                Vector2.Zero,
                scale,
                SpriteEffects.None,
                0
            );
        }
    }
}
