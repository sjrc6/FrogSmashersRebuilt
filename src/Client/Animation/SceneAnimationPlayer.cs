using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class SceneAnimationPlayer : IDisposable
{
    private const int Width = Renderer.Width;
    private const int Height = Renderer.Height;
    private readonly GraphicsDevice device;
    private readonly Assets assets;
    private readonly Audio? audio;
    private readonly SpriteBatch batch;
    private readonly SpriteMesh mesh = new();
    private RenderTarget2D target;
    private readonly Dictionary<string, SceneTransform> nodes = new(StringComparer.Ordinal);
    private readonly List<Visual> visuals = new();
    private readonly Dictionary<string, Visual> visualsByPath = new(StringComparer.Ordinal);
    private readonly List<ClipPlayer> clipPlayers = new();
    private readonly List<SoundTrack> soundTracks = new();
    private readonly List<SpriteParticle> particles = new();
    private MapData? scene;
    public Vector2 Camera { get; set; }
    public Color? PaletteColor { get; set; }

    public SceneAnimationPlayer(GraphicsDevice device, Assets assets, Audio? audio)
    {
        this.device = device;
        this.assets = assets;
        this.audio = audio;
        batch = new SpriteBatch(device);
        target = new RenderTarget2D(
            device,
            Width,
            Height,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            4,
            RenderTargetUsage.PreserveContents
        );
    }

    public void LoadScene(MapData definition)
    {
        StopAudio();
        scene = definition;
        visualsByPath.Clear();
        nodes.Clear();
        visuals.Clear();
        clipPlayers.Clear();
        particles.Clear();
        foreach (var source in scene.Nodes)
        {
            nodes[source.ObjectPath] = new SceneTransform(source);
        }

        foreach (var node in nodes.Values)
        {
            node.Parent = nodes.GetValueOrDefault(node.Source.ParentPath);
        }

        foreach (var node in nodes.Values)
        {
            SceneTransform.Resolve(node);
        }

        foreach (var source in scene.Sprites)
        {
            if (!nodes.TryGetValue(source.ObjectPath, out var node))
            {
                continue;
            }

            bool enabled = source.Active || !node.WorldActive;
            var visual = new Visual(source, node, enabled);
            visual.FlipX = Math.Sign(source.ScaleX) * Math.Sign(node.WorldScale.X);
            visual.FlipY = Math.Sign(source.ScaleY) * Math.Sign(node.WorldScale.Y);
            visuals.Add(visual);
        }

        foreach (var visual in visuals)
        {
            visualsByPath.TryAdd(visual.Source.ObjectPath, visual);
        }

        foreach (var source in scene.Audio)
        {
            soundTracks.Add(new SoundTrack(source));
        }
    }

    public SceneTransform? FindTransform(string path) => nodes.GetValueOrDefault(path);

    public Visual? FindVisual(string path) => visualsByPath.GetValueOrDefault(path);

    public void PlayClip(string root, string name, float started = 0)
    {
        if (assets.Data.Timelines.TryGetValue(name, out var clip))
        {
            clipPlayers.Add(new ClipPlayer(root, clip) { Started = started });
        }
    }

    public void SkipEvents(float elapsed)
    {
        foreach (var player in clipPlayers)
        {
            player.LastEventTime = elapsed;
        }
    }

    public void AdvanceAnimations(float dt)
    {
        foreach (var visual in visuals)
        {
            visual.Advance(dt);
        }
    }

    public void DispatchEvents(float elapsed, Action<TimelineEventData, float> dispatch)
    {
        int count = clipPlayers.Count;
        for (int index = 0; index < count; index++)
        {
            var player = clipPlayers[index];
            float current = elapsed - player.Started;
            foreach (var evt in player.Clip.Events)
            {
                if (evt.Time > player.LastEventTime && evt.Time <= current)
                {
                    dispatch(evt, player.Started + evt.Time);
                }
            }

            player.LastEventTime = current;
        }
    }

    public void AddParticle(Vector2 position, Vector2 velocity, string effect, Color color) =>
        particles.Add(new SpriteParticle(position, velocity, effect, color));

    public void AdvanceParticles(float dt)
    {
        foreach (var particle in particles)
        {
            particle.Age += dt;
            particle.Position += particle.Velocity * dt;
            var effect = assets.Data.Effects[particle.Effect];
            particle.Velocity +=
                new Vector2(
                    effect.Parameters.GetValueOrDefault("accelX"),
                    effect.Parameters.GetValueOrDefault("accelY")
                ) * dt;
            particle.Counter += dt;
            if (particle.Counter > effect.FrameSeconds)
            {
                particle.Counter -= effect.FrameSeconds;
                particle.Frame++;
            }
        }

        particles.RemoveAll(p => p.Age > 10 && !ParticleVisible(p));
    }

    public void ResetPose()
    {
        foreach (var node in nodes.Values)
        {
            node.Reset();
        }

        foreach (var visual in visuals)
        {
            visual.Reset();
        }

        foreach (var sound in soundTracks)
        {
            sound.Volume = sound.Source.Volume;
        }
    }

    public void ApplyClips(float elapsed)
    {
        foreach (var player in clipPlayers)
        {
            float time = Math.Clamp(elapsed - player.Started, 0, player.Clip.Duration);
            Apply(player.Root, player.Clip, time);
        }
    }

    public void ApplyLoop(string root, string name, float elapsed)
    {
        if (assets.Data.Timelines.TryGetValue(name, out var clip))
        {
            Apply(root, clip, elapsed % clip.Duration);
        }
    }

    public void ResolvePose()
    {
        if (audio != null)
        {
            audio.ListenerPosition = new Vector3(Camera, -10);
        }

        foreach (var node in nodes.Values)
        {
            SceneTransform.Resolve(node);
        }

        foreach (var sound in soundTracks)
        {
            bool active = nodes.TryGetValue(sound.Source.ObjectPath, out var node)
                ? node.WorldActive
                : sound.Source.Active;
            if (sound.Instance != null)
            {
                sound.Instance.Volume = Math.Clamp(
                    sound.Volume * (audio is { Enabled: true } ? audio.Volume : 0) * (active ? 1 : 0),
                    0,
                    1
                );
            }
        }
    }

    public void PlayInitialSounds()
    {
        foreach (var sound in soundTracks)
        {
            if (sound.Source.Active && sound.Source.PlayOnAwake)
            {
                StartSound(sound);
            }
        }
    }

    public void StopAudio()
    {
        foreach (var sound in soundTracks)
        {
            sound.Instance?.Stop();
            sound.Instance?.Dispose();
        }

        soundTracks.Clear();
    }

    private bool ParticleVisible(SpriteParticle particle)
    {
        var effect = assets.Data.Effects[particle.Effect];
        string id = particle.Frame < 0 ? effect.InitialSpriteId : effect.Frames[particle.Frame % effect.Frames.Length];
        if (scene == null || !assets.Data.Sprites.TryGetValue(id, out var sprite))
        {
            return false;
        }

        float width = sprite.RectWidth / sprite.PixelsPerUnit * Math.Abs(effect.ScaleX);
        float height = sprite.RectHeight / sprite.PixelsPerUnit * Math.Abs(effect.ScaleY);
        float halfWidth = scene.OrthoSize * Width / Height;
        return particle.Position.X + width * (1 - sprite.PivotX) >= Camera.X - halfWidth
            && particle.Position.X - width * sprite.PivotX <= Camera.X + halfWidth
            && particle.Position.Y + height * (1 - sprite.PivotY) >= Camera.Y - scene.OrthoSize
            && particle.Position.Y - height * sprite.PivotY <= Camera.Y + scene.OrthoSize;
    }

    private void StartSound(SoundTrack sound)
    {
        if (audio is not { Enabled: true } || audio.Volume <= 0 || sound.Instance != null)
        {
            return;
        }

        try
        {
            sound.Instance = assets.Sound(sound.Source.Path).CreateInstance();
            sound.Instance.IsLooped = sound.Source.Loop;
            sound.Instance.Volume = Math.Clamp(sound.Volume * audio.Volume, 0, 1);
            sound.Instance.Pitch = Math.Clamp(MathF.Log2(Math.Max(.01f, sound.Source.Pitch)), -1, 1);
            sound.Instance.Play();
        }
        catch (Exception ex) when (ex is NoAudioHardwareException or InvalidOperationException or DllNotFoundException)
        {
            sound.Instance?.Dispose();
            sound.Instance = null;
            Console.Error.WriteLine("Cinematic audio unavailable: " + ex.Message);
        }
    }

    private void Apply(string root, TimelineData clip, float time)
    {
        Span<float> sample = stackalloc float[4];
        foreach (var track in clip.Tracks)
        {
            string path = string.IsNullOrEmpty(track.ObjectPath) ? root : root + "/" + track.ObjectPath;
            if (!nodes.TryGetValue(path, out var node) || track.Keys.Count == 0)
            {
                continue;
            }

            var visual = visualsByPath.GetValueOrDefault(path);
            if (track.Kind == "sprite")
            {
                if (visual != null)
                {
                    visual.Sprite = AnimationCurve.SampleSprite(track.Keys, time);
                }

                continue;
            }

            var value = AnimationCurve.Sample(track.Keys, time, sample);
            switch (track.Kind)
            {
                case "position":
                    node.Position = Vector(value, node.Position);
                    break;
                case "scale":
                    node.Scale = Vector(value, node.Scale);
                    break;
                case "rotation":
                    if (value.Length >= 4)
                    {
                        node.Rotation = 2 * MathF.Atan2(value[2], value[3]);
                    }

                    break;
                case "euler":
                    if (value.Length >= 3)
                    {
                        node.Rotation = MathHelper.ToRadians(value[2]);
                    }

                    break;
                case "float":
                    float v = value.Length > 0 ? value[0] : 0;
                    switch (track.Attribute)
                    {
                        case "m_IsActive":
                            node.Active = v > .5f;
                            break;
                        case "m_Enabled":
                            if (visual != null)
                            {
                                visual.AnimationEnabled = v > .5f;
                            }

                            break;
                        case "m_Volume":
                            foreach (var sound in soundTracks.Where(s => s.Source.ObjectPath == path))
                            {
                                sound.Volume = v;
                            }

                            break;
                        case "m_Color.r":
                            if (visual != null)
                            {
                                visual.Color.X = v;
                            }

                            break;
                        case "m_Color.g":
                            if (visual != null)
                            {
                                visual.Color.Y = v;
                            }

                            break;
                        case "m_Color.b":
                            if (visual != null)
                            {
                                visual.Color.Z = v;
                            }

                            break;
                        case "m_Color.a":
                            if (visual != null)
                            {
                                visual.Color.W = v;
                            }

                            break;
                    }

                    break;
            }
        }
    }

    public Texture2D Draw(bool active)
    {
        float displayScale = Math.Min(
            device.PresentationParameters.BackBufferWidth / (float)Width,
            device.PresentationParameters.BackBufferHeight / (float)Height
        );
        int width = Math.Max(1, (int)(Width * displayScale));
        int height = Math.Max(1, (int)(Height * displayScale));
        if (target.Width != width || target.Height != height)
        {
            device.SetRenderTarget(null);
            target.Dispose();
            target = new RenderTarget2D(
                device,
                width,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.None,
                4,
                RenderTargetUsage.PreserveContents
            );
        }

        device.SetRenderTarget(target);
        device.Clear(Color.Black);
        if (!active || scene == null)
        {
            return target;
        }

        float ppu = Height / (scene.OrthoSize * 2);
        assets
            .SpriteEffect.Parameters["MatrixTransform"]
            .SetValue(Matrix.CreateOrthographicOffCenter(0, Width, Height, 0, 0, 1));
        batch.Begin(
            SpriteSortMode.Immediate,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            assets.SpriteEffect
        );
        var drawOrder = visuals
            .Select(v =>
                (
                    Visual: (Visual?)v,
                    Particle: (SpriteParticle?)null,
                    Layer: v.Source.SortingLayer,
                    Order: v.Source.Order,
                    Queue: RenderQueue(v.Source.Material),
                    Z: v.Transform.WorldPosition.Z
                )
            )
            .Concat(
                particles.Select(p =>
                {
                    var effect = assets.Data.Effects[p.Effect];
                    return (
                        Visual: (Visual?)null,
                        Particle: (SpriteParticle?)p,
                        Layer: effect.SortingLayer,
                        Order: effect.Order,
                        Queue: RenderQueue(effect.Material),
                        Z: 0f
                    );
                })
            );
        foreach (
            var entry in drawOrder
                .OrderBy(e => e.Layer)
                .ThenBy(e => e.Order)
                .ThenBy(e => e.Queue)
                .ThenByDescending(e => e.Z)
        )
        {
            if (entry.Particle is { } particle)
            {
                if (!assets.Data.Effects.TryGetValue(particle.Effect, out var effect) || effect.Frames.Length == 0)
                {
                    continue;
                }

                string sprite =
                    particle.Frame < 0 ? effect.InitialSpriteId : effect.Frames[particle.Frame % effect.Frames.Length];
                DrawSprite(
                    sprite,
                    particle.Position,
                    particle.Color.ToVector4(),
                    new Vector2(effect.ScaleX, effect.ScaleY),
                    0,
                    ppu,
                    1
                );
                continue;
            }

            var visual = entry.Visual!;
            if (
                !visual.Enabled
                || visual.Destroyed
                || !visual.Source.Visible
                || !visual.Transform.WorldActive
                || visual.Color.W <= 0
            )
            {
                continue;
            }

            bool palette = visual.Source.Material.Contains("SelectiveColorReplace", StringComparison.Ordinal);
            var color = palette && PaletteColor.HasValue ? PaletteColor.Value.ToVector4() : visual.Color;
            if (palette)
            {
                color.W = visual.Color.W;
            }

            DrawSprite(
                visual.Sprite,
                new Vector2(visual.Transform.WorldPosition.X, visual.Transform.WorldPosition.Y),
                color,
                new Vector2(visual.Transform.WorldScale.X * visual.FlipX, visual.Transform.WorldScale.Y * visual.FlipY),
                visual.Transform.WorldRotation,
                ppu,
                palette ? 1 : 0
            );
        }

        batch.End();
        return target;
    }

    private int RenderQueue(string material) =>
        assets.Data.Materials.TryGetValue(material, out var data) && data.RenderQueue >= 0 ? data.RenderQueue : 3000;

    private void DrawSprite(
        string id,
        Vector2 position,
        Vector4 color,
        Vector2 scale,
        float rotation,
        float ppu,
        float mode
    )
    {
        if (!assets.Data.Sprites.TryGetValue(id, out var source))
        {
            return;
        }

        assets.SpriteEffect.Parameters["Mode"].SetValue(mode);
        device.SamplerStates[0] = source.PointFilter ? SamplerState.PointClamp : SamplerState.LinearClamp;
        var rect = new Rectangle(source.RectX, source.RectY, source.RectWidth, source.RectHeight);
        var origin = new Vector2(rect.Width * source.PivotX, rect.Height * (1 - source.PivotY));
        var flip = SpriteEffects.None;
        if (scale.X < 0)
        {
            flip |= SpriteEffects.FlipHorizontally;
            origin.X = rect.Width - origin.X;
        }

        if (scale.Y < 0)
        {
            flip |= SpriteEffects.FlipVertically;
            origin.Y = rect.Height - origin.Y;
        }

        var screen = new Vector2(Width / 2 + (position.X - Camera.X) * ppu, Height / 2 - (position.Y - Camera.Y) * ppu);
        if (
            mesh.Draw(
                device,
                assets.SpriteEffect,
                assets.Texture(source.Path),
                source,
                screen,
                ppu,
                scale,
                rotation,
                PackColor(color)
            )
        )
        {
            return;
        }

        batch.Draw(
            assets.Texture(source.Path),
            screen,
            rect,
            PackColor(color),
            -rotation,
            origin,
            new Vector2(Math.Abs(scale.X), Math.Abs(scale.Y)) * ppu / Math.Max(.01f, source.PixelsPerUnit),
            flip,
            0
        );
    }

    private static Vector3 Vector(ReadOnlySpan<float> value, Vector3 fallback) =>
        value.Length >= 3 ? new Vector3(value[0], value[1], value[2]) : fallback;

    private static Color PackColor(Vector4 color) =>
        new(
            (byte)Math.Clamp(MathF.Round(color.X * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(color.Y * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(color.Z * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(color.W * 255), 0, 255)
        );

    public void Dispose()
    {
        StopAudio();
        device.SetRenderTarget(null);
        batch.Dispose();
        target.Dispose();
    }

    internal sealed class Visual(SceneSpriteData source, SceneTransform node, bool enabled)
    {
        public readonly SceneSpriteData Source = source;
        public readonly SceneTransform Transform = node;
        public readonly bool Enabled = enabled;
        public string Sprite = source.SpriteId;
        public Vector4 Color = new(source.Color[0], source.Color[1], source.Color[2], source.Color[3]);
        public int FlipX = 1;
        public int FlipY = 1;
        public bool AnimationEnabled = true;
        public bool AutomaticAnimation = true;
        public bool Destroyed { get; private set; }

        public int Frame = -1;
        private float counter;

        public void Advance(float dt)
        {
            if (
                Destroyed
                || !AnimationEnabled
                || !AutomaticAnimation
                || !Transform.WorldActive
                || Source.Frames.Length == 0
            )
            {
                return;
            }

            counter += dt;
            if (counter > Source.FrameSeconds)
            {
                counter -= Source.FrameSeconds;
                Frame++;
            }

            if (Source.PlayOnce && Frame >= Source.Frames.Length)
            {
                Destroyed = true;
            }
        }

        public void Reset()
        {
            Sprite = Source.SpriteId;
            if (Source.Frames.Length > 0)
            {
                if (Frame >= 0)
                {
                    Sprite = Source.Frames[
                        Source.PlayOnce ? Math.Min(Frame, Source.Frames.Length - 1) : Frame % Source.Frames.Length
                    ];
                }
            }

            Color = new Vector4(Source.Color[0], Source.Color[1], Source.Color[2], Source.Color[3]);
        }
    }

    private sealed class ClipPlayer(string root, TimelineData clip)
    {
        public readonly string Root = root;
        public readonly TimelineData Clip = clip;
        public float Started;
        public float LastEventTime = -.001f;
    }

    private sealed class SoundTrack(SceneAudioData source)
    {
        public readonly SceneAudioData Source = source;
        public SoundEffectInstance? Instance;
        public float Volume = source.Volume;
    }

    private sealed class SpriteParticle(Vector2 position, Vector2 velocity, string effect, Color color)
    {
        public Vector2 Position = position;
        public Vector2 Velocity = velocity;
        public readonly string Effect = effect;
        public readonly Color Color = color;
        public float Age;
        public float Counter;
        public int Frame = -1;
    }
}
