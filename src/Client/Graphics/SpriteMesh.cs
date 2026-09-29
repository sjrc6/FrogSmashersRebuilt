using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class SpriteMesh
{
    private VertexPositionColorTexture[] vertices = [];

    public bool Draw(
        GraphicsDevice device,
        Effect effect,
        Texture2D texture,
        SpriteData sprite,
        Vector2 screenPosition,
        float pixelsPerUnit,
        Vector2 scale,
        float rotation,
        Color color
    )
    {
        if (sprite.Vertices.Length == 0 || sprite.UV.Length != sprite.Vertices.Length || sprite.Triangles.Length == 0)
        {
            return false;
        }

        if (vertices.Length < sprite.Vertices.Length)
        {
            Array.Resize(ref vertices, sprite.Vertices.Length);
        }
        float c = MathF.Cos(rotation);
        float s = MathF.Sin(rotation);
        for (int i = 0; i < sprite.Vertices.Length; i++)
        {
            var p = sprite.Vertices[i];
            float x = p[0] * scale.X;
            float y = p[1] * scale.Y;
            var screen = screenPosition + new Vector2(x * c - y * s, -(x * s + y * c)) * pixelsPerUnit;
            vertices[i] = new(new(screen, 0), color, new(sprite.UV[i][0], 1 - sprite.UV[i][1]));
        }

        effect.CurrentTechnique.Passes[0].Apply();

        device.Textures[0] = texture;
        device.SamplerStates[0] = sprite.PointFilter ? SamplerState.PointClamp : SamplerState.LinearClamp;
        device.DrawUserIndexedPrimitives(
            PrimitiveType.TriangleList,
            vertices,
            0,
            sprite.Vertices.Length,
            sprite.Triangles,
            0,
            sprite.Triangles.Length / 3
        );
        return true;
    }
}
