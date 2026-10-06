namespace FrogSmashers.Core;

public enum MapRole
{
    Arena,
    Showdown,
    Presentation,
}

public sealed class MapData
{
    public string Id { get; set; } = "";
    public MapRole Role { get; set; }
    public string Name { get; set; } = "";
    public float CameraX { get; set; }
    public float CameraY { get; set; }
    public float OrthoSize { get; set; } = 15;
    public float[] BackgroundColor { get; set; } = [0, 0, 0, 1];
    public float ScreenTop { get; set; } = 18;
    public BoundsData KillBounds { get; set; } = new();
    public List<BoxData> Collision { get; set; } = new();
    public List<PointData> Spawns { get; set; } = new();
    public PointData FlySpawn { get; set; } = new();
    public PointData BeachBallSpawn { get; set; } = new() { Y = 4 };
    public List<SceneSpriteData> Sprites { get; set; } = new();
    public List<SceneEffectData> SceneEffects { get; set; } = new();
    public List<SceneAudioData> Audio { get; set; } = new();
    public List<ParallaxData> Parallax { get; set; } = new();
    public List<SceneNodeData> Nodes { get; set; } = new();
    public List<SceneAnimatorData> Animators { get; set; } = new();
    public List<ParticleEmitterData> ParticleEmitters { get; set; } = new();
    public Dictionary<string, float> PresentationParameters { get; set; } = new();
    public List<TimelineKeyData> CameraPanCurve { get; set; } = new();
    public string[] IntroParticleEffects { get; set; } = [];
    public string[] ParallaxFrogEffects { get; set; } = [];
}

public sealed class BoundsData
{
    public decimal Left { get; set; } = -50;
    public decimal Right { get; set; } = 50;
    public decimal Bottom { get; set; } = -30;
    public decimal Top { get; set; } = 40;
}

public sealed class PointData
{
    public decimal X { get; set; }
    public decimal Y { get; set; }
}

public sealed class BoxData
{
    public string Name { get; set; } = "";
    public decimal X { get; set; }
    public decimal Y { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
    public bool OneWay { get; set; }
}
