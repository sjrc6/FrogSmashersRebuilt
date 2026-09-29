namespace FrogSmashers.Core;

public sealed class MaterialData
{
    public string Shader { get; set; } = "";
    public Dictionary<string, float> Parameters { get; set; } = new();
    public Dictionary<string, float[]> Colors { get; set; } = new();
    public Dictionary<string, string> Textures { get; set; } = new();
    public Dictionary<string, float[]> TextureTransforms { get; set; } = new();
    public int RenderQueue { get; set; } = -1;
}
