using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed class DistanceFieldFont
{
    private readonly Texture2D texture;
    private readonly Dictionary<int, Glyph> glyphs;
    private readonly float baseline;
    private readonly float centeredBaseline;
    private readonly float normalizedDistanceRange;

    public float LineHeight { get; }

    private readonly record struct Glyph(Rectangle Region, Vector2 Offset, Vector2 Scale, float Advance);

    public DistanceFieldFont(Texture2D texture, string definitionPath)
        : this(texture, FontDefinition.Load(definitionPath)) { }

    public DistanceFieldFont(Texture2D texture, FontDefinition definition)
    {
        if (
            definition.AtlasSize != texture.Width
            || definition.AtlasSize != texture.Height
            || definition.DistanceRange <= 0
            || definition.LineHeight <= 0
        )
        {
            throw new InvalidDataException("Invalid font atlas dimensions or metrics");
        }

        this.texture = texture;
        baseline = definition.Baseline;
        centeredBaseline = definition.CenteredBaseline;
        normalizedDistanceRange = definition.DistanceRange / definition.AtlasSize;
        LineHeight = definition.LineHeight;
        glyphs = definition.Glyphs.ToDictionary(entry => entry.Key, entry => CreateGlyph(entry.Value));
    }

    private static Glyph CreateGlyph(FontGlyph data)
    {
        if (data.Region.Length == 0 && data.Bounds.Length == 0)
        {
            return new Glyph(Rectangle.Empty, Vector2.Zero, Vector2.Zero, data.Advance);
        }

        if (data.Region.Length != 4 || data.Bounds.Length != 4 || data.Region[2] <= 0 || data.Region[3] <= 0)
        {
            throw new InvalidDataException("Invalid font glyph region");
        }

        return new Glyph(
            new Rectangle(data.Region[0], data.Region[1], data.Region[2], data.Region[3]),
            new Vector2(data.Bounds[0], data.Bounds[1]),
            new Vector2(data.Bounds[2] / data.Region[2], data.Bounds[3] / data.Region[3]),
            data.Advance
        );
    }

    public Vector2 Measure(string text, float scale = 1)
    {
        float lineWidth = 0;
        float width = 0;
        int lines = 1;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                width = Math.Max(width, lineWidth);
                lineWidth = 0;
                lines++;
            }
            else if (glyphs.TryGetValue(character, out var glyph))
            {
                lineWidth += glyph.Advance * scale;
            }
        }

        return new Vector2(Math.Max(width, lineWidth), lines * LineHeight * scale);
    }

    public void Draw(
        SpriteBatch batch,
        string text,
        Vector2 position,
        Color color,
        float scale = 1,
        bool center = false
    )
    {
        position.Y += baseline * scale;
        DrawAtBaseline(batch, text, position, color, scale, center);
    }

    public void DrawCenteredVertical(
        SpriteBatch batch,
        string text,
        Vector2 position,
        Color color,
        float scale = 1,
        bool center = false
    )
    {
        position.Y += centeredBaseline * scale;
        DrawAtBaseline(batch, text, position, color, scale, center);
    }

    private void DrawAtBaseline(SpriteBatch batch, string text, Vector2 position, Color color, float scale, bool center)
    {
        if (center)
        {
            position.X -= Measure(text, scale).X * 0.5f;
        }

        var cursor = position;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                cursor.X = position.X;
                cursor.Y += LineHeight * scale;
                continue;
            }

            if (!glyphs.TryGetValue(character, out var glyph))
            {
                continue;
            }

            if (!glyph.Region.IsEmpty)
            {
                // UI has no depth testing; the shader uses layer depth for each atlas's distance range.
                batch.Draw(
                    texture,
                    cursor + glyph.Offset * scale,
                    glyph.Region,
                    color,
                    0,
                    Vector2.Zero,
                    glyph.Scale * scale,
                    SpriteEffects.None,
                    normalizedDistanceRange
                );
            }

            cursor.X += glyph.Advance * scale;
        }
    }
}
