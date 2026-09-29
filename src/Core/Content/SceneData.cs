namespace FrogSmashers.Core;

public sealed class SceneSpriteData
{
    public string Name { get; set; } = "";
    public string ObjectPath { get; set; } = "";
    public long SourceId { get; set; }
    public bool Active { get; set; } = true;
    public bool Visible { get; set; } = true;
    public int Layer { get; set; }
    public string[] Frames { get; set; } = [];
    public float FrameSeconds { get; set; } = .1f;
    public string SpriteId { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float Rotation { get; set; }
    public int SortingLayer { get; set; }
    public int Order { get; set; }
    public float[] Color { get; set; } = [1, 1, 1, 1];
    public string Material { get; set; } = "";
    public bool PlayOnce { get; set; }
}

public sealed class SceneEffectData
{
    public string Material { get; set; } = "";
    public string ObjectPath { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Rotation { get; set; }
    public bool Active { get; set; }
}

public sealed class SceneNodeData
{
    public string ObjectPath { get; set; } = "";
    public string ParentPath { get; set; } = "";
    public float[] LocalPosition { get; set; } = [0, 0, 0];
    public float[] LocalScale { get; set; } = [1, 1, 1];
    public float LocalRotation { get; set; }
    public bool Active { get; set; }
}

public sealed class SceneAnimatorData
{
    public string ObjectPath { get; set; } = "";
    public string DefaultClip { get; set; } = "";
    public string[] Clips { get; set; } = [];
}

public sealed class ParallaxData
{
    public string[] Effects { get; set; } = [];
    public float[] SpeedMin { get; set; } = [0, 0];
    public float[] SpeedMax { get; set; } = [0, 0];
    public float[] SpawnRangeX { get; set; } = [0, 0];
    public float[] SpawnRangeY { get; set; } = [0, 0];
    public float Depth { get; set; }
    public float Probability { get; set; }
    public bool DontScale { get; set; }
    public bool ParentToCamera { get; set; }
    public bool DropFrog { get; set; }
}
