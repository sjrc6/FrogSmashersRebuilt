using System.Text.Json;

namespace FrogSmashers.Client;

public sealed class FontDefinition
{
    public string Texture { get; set; } = "";
    public int AtlasSize { get; set; }
    public float DistanceRange { get; set; }
    public float LineHeight { get; set; }
    public float Baseline { get; set; }
    public float CenteredBaseline { get; set; }
    public Dictionary<int, FontGlyph> Glyphs { get; set; } = new();

    public static FontDefinition Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<FontDefinition>(stream)
            ?? throw new InvalidDataException($"Empty font definition: {path}");
    }
}
