using System.Text.Json;

namespace FrogSmashers.Core;

public sealed class GameContent
{
    public int Version { get; set; } = 1;
    public Dictionary<string, SpriteData> Sprites { get; set; } = new();
    public List<MapData> Maps { get; set; } = new();
    public Dictionary<string, string[]> Animations { get; set; } = new();
    public Dictionary<string, EffectData> Effects { get; set; } = new();
    public Dictionary<string, string[]> Sounds { get; set; } = new();
    public Dictionary<string, SoundSettingsData> SoundSettings { get; set; } = new();
    public Dictionary<string, MaterialData> Materials { get; set; } = new();
    public Dictionary<string, TimelineData> Timelines { get; set; } = new();
    public Dictionary<string, MapData> PresentationScenes { get; set; } = new();
    public Dictionary<string, string> AssetHashes { get; set; } = new();
    public Dictionary<string, decimal> CharacterParameters { get; set; } = new();
    public Dictionary<string, float> CharacterVisualParameters { get; set; } = new();
    public Dictionary<string, float> EffectsParameters { get; set; } = new();
    public float[][] EffectsColors { get; set; } = [];
    public EffectData TongueTip { get; set; } = new();
    public string TongueLineMaterial { get; set; } = "";
    public float TongueLineWidth { get; set; } = .5f;
    public SceneAudioData FlightAudio { get; set; } = new();
    public float CharacterOffsetX { get; set; }
    public float CharacterOffsetY { get; set; }
    public float CharacterScaleX { get; set; } = 1;
    public float CharacterScaleY { get; set; } = 1;
    public string ContentHash { get; set; } = "";

    public string ComputeGameplayHash() => GameplayData.Hash(this);

    public static GameContent Load(string path) =>
        JsonSerializer.Deserialize<GameContent>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        ) ?? throw new InvalidDataException("Empty content manifest");
}
