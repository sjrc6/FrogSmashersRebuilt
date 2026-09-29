namespace FrogSmashers.Client;

public sealed class FontGlyph
{
    public float Advance { get; set; }
    public int[] Region { get; set; } = [];
    public float[] Bounds { get; set; } = [];
}
