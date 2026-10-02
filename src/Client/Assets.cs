using FrogSmashers.Core;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed class Assets : IDisposable
{
    private readonly ContentManager content;
    private readonly Dictionary<string, SoundEffect> sounds = new();
    private readonly Dictionary<string, DecodedSound> soundData = new();
    private readonly Dictionary<string, SpatialSoundData> spatialSounds = new();

    public string Root { get; }
    public GameContent Data { get; }
    public Effect SpriteEffect { get; }
    public Effect DistortionEffect { get; }
    public Texture2D White { get; }
    public BitmapFont Font { get; }
    public BitmapFont ScoreFont { get; }

    public Assets(ContentManager content, string root)
    {
        this.content = content;
        Root = root;
        content.RootDirectory = root;
        Data = GameContent.Load(Path.Combine(root, "content.json"));
        White = Texture("UI/white");
        SpriteEffect = content.Load<Effect>("Effects/Sprite");
        DistortionEffect = content.Load<Effect>("Effects/Distortion");
        Font = LoadFont("retroville", 14, pixelAligned: true);
        ScoreFont = LoadFont("arcade", 10, pixelAligned: false);
    }

    public Texture2D Texture(string assetName) => content.Load<Texture2D>(assetName);

    public string? Frame(string animation, int frame = 0, bool clamp = false)
    {
        if (!Data.Animations.TryGetValue(animation, out var frames) || frames.Length == 0)
        {
            return null;
        }

        int index = clamp ? Math.Clamp(frame, 0, frames.Length - 1) : Math.Abs(frame) % frames.Length;
        return frames[index];
    }

    public SoundEffect Sound(string path)
    {
        if (!sounds.TryGetValue(path, out var sound))
        {
            var data = SoundPcm(path);
            sound = new SoundEffect(data.Pcm, data.SampleRate, (AudioChannels)data.Channels);
            sounds.Add(path, sound);
        }

        return sound;
    }

    public DecodedSound SoundPcm(string path)
    {
        if (!soundData.TryGetValue(path, out var data))
        {
            data = DecodedSound.Read(Path.Combine(Root, path));
            soundData.Add(path, data);
        }

        return data;
    }

    internal SpatialSoundData SpatialSound(string path)
    {
        if (!spatialSounds.TryGetValue(path, out var data))
        {
            data = new(SoundPcm(path));
            spatialSounds.Add(path, data);
        }
        return data;
    }

    private BitmapFont LoadFont(string name, int displayHeight, bool pixelAligned)
    {
        var definition = FontDefinition.Load(Path.Combine(Root, "UI", name + ".json"));
        return new BitmapFont(Texture(definition.Texture), definition, displayHeight, pixelAligned);
    }

    public void Dispose()
    {
        foreach (var sound in sounds.Values)
        {
            sound.Dispose();
        }

        sounds.Clear();
        soundData.Clear();
        spatialSounds.Clear();
    }
}
