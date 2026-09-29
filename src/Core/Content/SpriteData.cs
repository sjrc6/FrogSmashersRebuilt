namespace FrogSmashers.Core;

public sealed class SpriteData
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int RectX { get; set; }
    public int RectY { get; set; }
    public int RectWidth { get; set; }
    public int RectHeight { get; set; }
    public float PivotX { get; set; } = .5f;
    public float PivotY { get; set; } = .5f;
    public float PixelsPerUnit { get; set; } = 16;
    public bool PointFilter { get; set; } = true;
    public float[][] Vertices { get; set; } = [];
    public float[][] UV { get; set; } = [];
    public int[] Triangles { get; set; } = [];
}
