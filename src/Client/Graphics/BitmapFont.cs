using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed class BitmapFont
{
    private readonly Texture2D texture;
    private readonly Dictionary<int, Glyph> glyphs;
    private readonly int lineHeight;
    private readonly int capHeight;
    private readonly float pixelSize;
    private readonly bool pixelAligned;

    private readonly record struct Glyph(Rectangle Region, Vector2 Offset, int Advance);

    public BitmapFont(Texture2D texture, FontDefinition definition, int displayHeight, bool pixelAligned)
    {
        if (definition.LineHeight <= 0 || definition.CapHeight <= 0)
            throw new InvalidDataException("Invalid bitmap font metrics");
        this.texture = texture;
        lineHeight = definition.LineHeight;
        capHeight = definition.CapHeight;
        pixelSize = displayHeight / (float)capHeight;
        this.pixelAligned = pixelAligned;
        glyphs = definition.Glyphs.ToDictionary(entry => entry.Key, entry => CreateGlyph(entry.Value));
    }

    private Glyph CreateGlyph(FontGlyph data)
    {
        if (data.Region.Length == 0)
            return new Glyph(Rectangle.Empty, Vector2.Zero, data.Advance);
        if (data.Region.Length != 4 || data.Offset.Length != 2)
            throw new InvalidDataException("Invalid bitmap glyph region");
        var region = new Rectangle(data.Region[0], data.Region[1], data.Region[2], data.Region[3]);
        if (region.IsEmpty || !texture.Bounds.Contains(region))
            throw new InvalidDataException("Bitmap glyph lies outside its atlas");
        return new Glyph(region, new Vector2(data.Offset[0], data.Offset[1]), data.Advance);
    }

    private Glyph Character(char character) => glyphs.GetValueOrDefault(character, glyphs['?']);

    private Vector2 PixelScale(float scale)
    {
        if (!pixelAligned)
            return new Vector2(pixelSize * scale);
        var viewport = texture.GraphicsDevice.Viewport;
        var outputScale = new Vector2(viewport.Width / (float)Renderer.Width, viewport.Height / (float)Renderer.Height);
        float pixels = MathF.Round(
            pixelSize * scale * Math.Min(outputScale.X, outputScale.Y),
            MidpointRounding.AwayFromZero
        );
        return new Vector2(Math.Max(1, pixels)) / outputScale;
    }

    public Vector2 Measure(string text, float scale = 1)
    {
        if (scale <= 0)
            return Vector2.Zero;
        var lines = text.Split('\n');
        int width = lines.Max(line => line.Sum(character => Character(character).Advance));
        return new Vector2(width, capHeight + (lines.Length - 1) * lineHeight) * PixelScale(scale);
    }

    public string Wrap(string text, float width, int maxLines = int.MaxValue)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate).X <= width)
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0)
                {
                    lines.Add(line);
                    line = "";
                }
                foreach (char character in word)
                {
                    if (line.Length > 0 && Measure(line + character).X > width)
                    {
                        lines.Add(line);
                        line = "";
                    }
                    line += character;
                }
            }
            lines.Add(line);
        }
        if (lines.Count > maxLines)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            string last = lines[^1];
            while (last.Length > 0 && Measure(last + "...").X > width)
                last = last[..^1];
            lines[^1] = last.TrimEnd() + "...";
        }
        return string.Join('\n', lines);
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
        if (scale <= 0)
            return;
        var pixels = PixelScale(scale);
        if (pixelAligned)
            position.Y = MathF.Round(position.Y / pixels.Y) * pixels.Y;
        foreach (string line in text.Split('\n'))
        {
            float left = position.X - (center ? Measure(line, scale).X / 2 : 0);
            if (pixelAligned)
                left = MathF.Round(left / pixels.X) * pixels.X;
            var cursor = new Vector2(left, position.Y);
            foreach (char character in line)
            {
                var glyph = Character(character);
                if (!glyph.Region.IsEmpty)
                    batch.Draw(
                        texture,
                        cursor + glyph.Offset * pixels,
                        glyph.Region,
                        color,
                        0,
                        Vector2.Zero,
                        pixels,
                        SpriteEffects.None,
                        0
                    );
                cursor.X += glyph.Advance * pixels.X;
            }
            position.Y += lineHeight * pixels.Y;
        }
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
        position.Y -= Measure(text, scale).Y / 2;
        Draw(batch, text, position, color, scale, center);
    }
}
