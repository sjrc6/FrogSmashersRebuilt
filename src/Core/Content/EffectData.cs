namespace FrogSmashers.Core;

public sealed class EffectData
{
    public string InitialSpriteId { get; set; } = "";
    public float Z { get; set; }
    public int Order { get; set; }
    public int SortingLayer { get; set; }
    public string[] Frames { get; set; } = [];
    public float FrameSeconds { get; set; } = .04f;
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public string Material { get; set; } = "";
    public Dictionary<string, string[]> Animations { get; set; } = new();
    public bool PlayOnce { get; set; } = true;
    public Dictionary<string, float> Parameters { get; set; } = new();
    public float[][] Colors { get; set; } = [];
    public float[] Color { get; set; } = [1, 1, 1, 1];
}
