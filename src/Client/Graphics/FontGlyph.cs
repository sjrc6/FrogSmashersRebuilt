namespace FrogSmashers.Client;

public sealed class FontGlyph
{
    public int Advance { get; set; }
    public int[] Region { get; set; } = [];
    public int[] Offset { get; set; } = [];
}
