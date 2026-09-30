using System.Text.Json;

namespace FrogSmashers.Client;

public sealed class FontDefinition
{
    public string Texture { get; set; } = "";
    public int LineHeight { get; set; }
    public int CapHeight { get; set; }
    public Dictionary<int, FontGlyph> Glyphs { get; set; } = new();

    public static FontDefinition Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<FontDefinition>(stream)
            ?? throw new InvalidDataException($"Empty font definition: {path}");
    }
}
