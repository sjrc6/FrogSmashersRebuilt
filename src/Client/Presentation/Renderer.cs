using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed partial class Renderer : IDisposable
{
    public static Color[] PlayerColors => PlayerPalette.Colors;
    public const int Width = 1280;
    public const int Height = 720;
    private readonly Assets assets;
    private readonly SpriteCanvas canvas;
    private readonly GameCamera cameraController;
    private readonly ScoreDisplay scores;
    private readonly NineSlicePanel menuPanel;
    internal ScoreDisplay Scores => scores;
    internal SpriteCanvas Canvas => canvas;
    internal GameCamera Camera => cameraController;
    private readonly FrameCompositor compositor;
    private readonly EffectSystem effects;
    internal EffectSystem Effects => effects;
    private readonly HashSet<long> seen = new();
    private readonly Queue<long> eventOrder = new();
    private readonly List<ParallaxPiece> parallax = new();
    private readonly Random cosmetics = new(7811);
    private MapData? activeMap;
    private float parallaxDelay;
    private readonly Dictionary<long, SpriteAnimation> sceneClocks = new();
    private SpriteAnimation flyClock = new();
    private bool flyWasActive;
    private readonly List<Impact> impacts = new();
    private float frameSeconds = 1 / 60f;
    private float time;
    private double sceneTime;
    private string mapId = "";
    private int mapRound;
    public bool ShakeEnabled
    {
        get => cameraController.ShakeEnabled;
        set => cameraController.ShakeEnabled = value;
    }
    public bool ShowColliders { get; set; }

    public SpriteBatch Batch => canvas.Batch;
    public Assets Assets => assets;

    public Renderer(GraphicsDevice device, Assets assets)
    {
        this.assets = assets;
        cameraController = new GameCamera(assets.Data.EffectsParameters);
        canvas = new SpriteCanvas(device, assets, cameraController);
        scores = new ScoreDisplay(assets, canvas, cameraController);
        menuPanel = new NineSlicePanel(assets.Texture("UI/menu-panel"));
        effects = new EffectSystem(assets, canvas, cameraController, cosmetics, ColorFor);
        compositor = new FrameCompositor(device, assets);
        var projection = Matrix.CreateOrthographicOffCenter(0, Width, Height, 0, 0, 1);
        assets.SpriteEffect.Parameters["MatrixTransform"].SetValue(projection);
        var palette = assets.Data.Materials["SelectiveColorReplace"].Colors;
        assets
            .SpriteEffect.Parameters["PaletteReplace"]
            .SetValue(new Vector2(palette["_ReplaceColor"][0], palette["_SecondReplaceColor"][0]));
    }

    public void Update(float dt)
    {
        frameSeconds = dt;
        if (dt <= 0)
        {
            return;
        }

        time += dt;
        sceneTime += dt;
        scores.Update(dt);
        effects.Update(dt, time);
        foreach (var i in impacts)
        {
            i.Age += dt;
        }

        impacts.RemoveAll(i => i.Age > i.Life);
        if (activeMap != null)
        {
            foreach (var sprite in activeMap.Sprites.Where(s => s.Active && s.Frames.Length > 0))
            {
                if (!sceneClocks.TryGetValue(sprite.SourceId, out var clock))
                {
                    sceneClocks[sprite.SourceId] = clock = new();
                }

                clock.Step(dt, sprite.FrameSeconds, sprite.Frames.Length, sprite.PlayOnce);
            }
        }

        foreach (var piece in parallax)
        {
            piece.Age += dt;
            piece.Position += piece.Velocity * dt;
            var data = assets.Data.Effects[piece.Name];
            piece.Clock.Step(dt, data.FrameSeconds, data.Frames.Length, data.PlayOnce);
        }

        foreach (
            var piece in parallax
                .Where(p =>
                    p.Age > 10
                    && !canvas.SpriteVisible(
                        p.Clock.Frame < 0
                            ? assets.Data.Effects[p.Name].InitialSpriteId
                            : assets.Data.Effects[p.Name].Frames[
                                p.Clock.Frame % assets.Data.Effects[p.Name].Frames.Length
                            ],
                        p.Position + (p.FollowCamera ? cameraController.Position : Vector2.Zero),
                        p.Scale,
                        0
                    )
                )
                .ToArray()
        )
        {
            if (piece.DropFrog && activeMap?.ParallaxFrogEffects.Length > 0 && RandomRange(0, 1) < .5f)
            {
                var names = activeMap.ParallaxFrogEffects;
                var frog = effects.Add(
                    names[cosmetics.Next(names.Length)],
                    piece.Position + (piece.FollowCamera ? cameraController.Position : Vector2.Zero),
                    PlayerColors[0],
                    -1,
                    new EffectSpawn { Velocity = new(RandomRange(-5, 5), 0) }
                );
                if (frog != null)
                {
                    frog.FrameDelay = .1f;
                    frog.Z = piece.Z;
                    frog.Queue = 3000;
                }
            }

            parallax.Remove(piece);
        }

        if (activeMap?.Parallax.Count > 0)
        {
            parallaxDelay -= dt;
            if (parallaxDelay < 0 && SpawnParallax(activeMap))
            {
                parallaxDelay = RandomRange(0, .25f);
            }
        }
    }

    public void Reset()
    {
        effects.Reset();
        ballPose = new();
        impacts.Clear();
        parallax.Clear();
        seen.Clear();
        eventOrder.Clear();
        scores.Reset();
        crewLayout.Reset();
        smokeEmitters.Clear();
        mapId = "";
        activeMap = null;
        cameraController.Reset();
    }

    public void Rewind(long fromTick)
    {
        effects.Rewind(fromTick);
        ballPose.Rewind(fromTick);
        impacts.RemoveAll(e => e.Tick >= fromTick);
        scores.Rewind(fromTick);
        seen.RemoveWhere(id => (id >> 12) >= fromTick);
        eventOrder.Clear();
        foreach (var id in seen)
        {
            eventOrder.Enqueue(id);
        }
    }

    public void Consume(IEnumerable<SimulationEvent> events, World world, float age = 0)
    {
        SetMap(world.Map, world.Match.RoundNumber);
        foreach (var e in events)
        {
            if (!seen.Add(e.Id))
            {
                continue;
            }

            eventOrder.Enqueue(e.Id);
            while (eventOrder.Count > 4096)
            {
                seen.Remove(eventOrder.Dequeue());
            }

            var p = new Vector2(e.X.ToFloat(), e.Y.ToFloat());
            var color = e.Player >= 0 ? ColorFor(world, e.Player) : Color.White;
            var player = e.Player >= 0 ? world.Players[e.Player] : null;
            int facing = player?.Facing ?? 1;
            scores.Consume(e, age);
            switch (e.Kind)
            {
                case SimulationEventKind.Spawn:
                    characters[e.Player] = new CharacterPresentation();
                    SpawnPuff(world.Map, p, color, e.Tick, age);
                    break;
                case SimulationEventKind.Jump:
                    var jumpSide = e.SurfaceSide == 0 ? Contact(world.Map, p) : Surface(e.SurfaceSide);
                    effects.Add(
                        "JumpPuffStraight",
                        p + jumpSide.Offset,
                        Color.White,
                        e.Tick,
                        new EffectSpawn { Age = age, Rotation = jumpSide.Rotation }
                    );
                    break;
                case SimulationEventKind.Land:
                    if (Math.Abs(e.SurfaceSide) == 1)
                    {
                        effects.Add(
                            "WallPuff",
                            p + Surface(e.SurfaceSide).Offset,
                            Color.White,
                            e.Tick,
                            new EffectSpawn { Age = age, Rotation = Surface(e.SurfaceSide).Rotation }
                        );
                    }
                    else
                    {
                        effects.Add(
                            "JumpPuffSkew",
                            p,
                            Color.White,
                            e.Tick,
                            new EffectSpawn { Age = age, Facing = facing }
                        );
                    }

                    break;
                case SimulationEventKind.Footstep:
                    effects.Add("RunDustPuff", p, Color.White, e.Tick, new EffectSpawn { Age = age, Facing = facing });
                    break;
                case SimulationEventKind.Turn:
                    effects.Add(
                        "TurnAroundPuff",
                        p,
                        Color.White,
                        e.Tick,
                        new EffectSpawn { Age = age, Facing = facing }
                    );
                    break;
                case SimulationEventKind.BeachBallBounce:
                case SimulationEventKind.Bounce:
                    if (e.Kind == SimulationEventKind.BeachBallBounce)
                    {
                        if (e.Strength <= 5)
                            break;
                        ballPose.Bounce(assets, world.BeachBall, e, age);
                    }
                    var side = e.SurfaceSide == 0 ? Contact(world.Map, p) : Surface(e.SurfaceSide);
                    var offset =
                        e.Kind == SimulationEventKind.BeachBallBounce
                            ? (side.Offset - Vector2.UnitY) * BeachBallState.Radius.ToFloat()
                            : side.Offset;
                    effects.Add(
                        side.Offset.X != 0 ? "WallPuff" : "BouncePuff",
                        p + offset,
                        Color.White,
                        e.Tick,
                        new EffectSpawn { Age = age, Rotation = side.Rotation }
                    );
                    break;
                case SimulationEventKind.Swing:
                    if (player != null && e.Strength.ToFloat() > .5f)
                    {
                        var dir = new Vector2(player.AttackX.ToFloat(), player.AttackY.ToFloat());
                        float reach =
                            dir.Y == 0 ? 3
                            : dir.X == 0 && dir.Y > 0 ? 3.75f
                            : 2.75f;
                        effects.Add(
                            "ShingEffect",
                            p + Vector2.UnitY + dir * reach + (dir.Y == 0 ? Vector2.UnitY * .2f : Vector2.Zero),
                            Color.White,
                            e.Tick,
                            new EffectSpawn { Age = age, Rotation = MathF.Atan2(dir.Y, dir.X) - MathF.PI / 2 }
                        );
                    }

                    break;
                case SimulationEventKind.BeachBallHit:
                case SimulationEventKind.Hit:
                    if (e.Kind == SimulationEventKind.BeachBallHit)
                        ballPose.ClearSquish();
                    if (e.HitKind == HitKind.Tongue)
                    {
                        break;
                    }

                    float life = e.HitstopSeconds.ToFloat();
                    var hitPosition = new Vector2(e.HitEffectX.ToFloat(), e.HitEffectY.ToFloat());
                    effects.Add(
                        "HitEffect",
                        hitPosition,
                        Color.White,
                        e.Tick,
                        new EffectSpawn
                        {
                            Age = age,
                            Scale = 1 + life * .4f,
                            Life = life,
                        }
                    );
                    effects.Add(
                        "HitStar",
                        hitPosition,
                        Color.White,
                        e.Tick,
                        new EffectSpawn
                        {
                            Age = age,
                            Scale = 1 + life * .4f,
                            Life = life,
                        }
                    );
                    if (e.Power >= Fixed.One)
                    {
                        effects.Add(
                            "HitStarPowerHit",
                            hitPosition,
                            Color.White,
                            e.Tick,
                            new EffectSpawn
                            {
                                Age = age,
                                Scale = 1.5f + life * .4f,
                                Life = life,
                            }
                        );
                    }

                    var direction = new Vector2(e.VelocityX.ToFloat(), e.VelocityY.ToFloat());
                    impacts.Add(
                        new()
                        {
                            Tick = e.Tick,
                            Position = e.Kind == SimulationEventKind.BeachBallHit ? p : p + Vector2.UnitY,
                            Direction = new Vector2(-direction.X, -direction.Y),
                            Life = .1f + life * .1f,
                            Age = age,
                        }
                    );
                    if (e.Power >= Fixed.One)
                    {
                        cameraController.Shake(Vector2.Normalize(direction), e.ComboHits * .75f);
                    }

                    break;
                case SimulationEventKind.TongueHit:
                    var tip = p;
                    effects.Add("TongueHitEffect", tip, Color.White, e.Tick, new EffectSpawn { Age = age });
                    break;
                case SimulationEventKind.Death:

                    effects.RemoveCharacter(e.Player);
                    ScorePlume(world, p, color, e, age);

                    if (p.Y > (float)world.Map.KillBounds.Top)
                    {
                        effects.Add("KnockedUpEffect", p, color, e.Tick, new EffectSpawn { Age = age });
                    }

                    break;
            }
        }
    }

    private static (Vector2 Offset, float Rotation) Surface(int side) =>
        side switch
        {
            -1 => (new Vector2(-1, 1), -MathF.PI / 2),
            1 => (new Vector2(1, 1), MathF.PI / 2),
            2 => (new Vector2(0, 2), MathF.PI),
            _ => (Vector2.Zero, 0),
        };

    private void SetMap(MapData map, int round = 0)
    {
        if (mapId == map.Id && mapRound == round)
        {
            return;
        }

        effects.Reset();
        ballPose = new();
        impacts.Clear();
        parallax.Clear();
        sceneClocks.Clear();
        smokeEmitters.Clear();
        flyClock = new();
        flyWasActive = false;
        parallaxDelay = 0;
        sceneTime = 0;
        activeMap = map;
        mapId = map.Id;
        mapRound = round;
        cameraController.Reset(map);
        confettiCounter = 0;
        for (int i = 0; i < 8; i++)
        {
            characters[i] = new CharacterPresentation();
        }
    }

    private static (Vector2 Offset, float Rotation) Contact(MapData map, Vector2 p)
    {
        float nearest = float.MaxValue;
        Vector2 offset = Vector2.Zero;
        float rotation = 0;
        foreach (var box in map.Collision)
        {
            float l = (float)(box.X - box.Width / 2);
            float r = (float)(box.X + box.Width / 2);
            float b = (float)(box.Y - box.Height / 2);
            float t = (float)(box.Y + box.Height / 2);
            void Try(float distance, Vector2 o, float angle)
            {
                if (distance < nearest)
                {
                    nearest = distance;
                    offset = o;
                    rotation = angle;
                }
            }

            if (p.X + 1 >= l && p.X - 1 <= r)
            {
                Try(MathF.Abs(p.Y - t), Vector2.Zero, 0);
                if (!box.OneWay)
                {
                    Try(MathF.Abs(p.Y + 2 - b), new(0, 2), MathF.PI);
                }
            }

            if (!box.OneWay && p.Y + 2 >= b && p.Y <= t)
            {
                Try(MathF.Abs(p.X + 1 - l), new(1, 1), MathF.PI / 2);
                Try(MathF.Abs(p.X - 1 - r), new(-1, 1), -MathF.PI / 2);
            }
        }

        return (offset, rotation);
    }

    private void ScorePlume(World world, Vector2 p, Color color, SimulationEvent ev, float age)
    {
        int points = ev.ScoreDelta;
        if (ev.Other >= 0)
        {
            color = ColorFor(world, ev.Other);
        }

        var edge = new Vector2(Math.Clamp(p.X, -31, 31), Math.Clamp(p.Y, -17, 17));
        float rotation;
        if (p.X < (float)world.Map.KillBounds.Left)
        {
            edge.X = -32;
            rotation = 0;
        }
        else if (p.X > (float)world.Map.KillBounds.Right)
        {
            edge.X = 32;
            rotation = MathF.PI;
        }
        else if (p.Y > (float)world.Map.KillBounds.Top)
        {
            edge.Y = 18;
            rotation = -MathF.PI / 2;
        }
        else
        {
            edge.Y = -18;
            rotation = MathF.PI / 2;
        }

        effects.Add(
            "SideScorePlum",
            edge,
            color,
            ev.Tick,
            new EffectSpawn
            {
                Age = age,
                Rotation = rotation,
                Points = points,
                CameraRelative = true,
            }
        );
        cameraController.Shake(-new Vector2(MathF.Cos(rotation), MathF.Sin(rotation)), ev.Other >= 0 ? points : -1);
    }

    private bool SpawnParallax(MapData map)
    {
        foreach (var piece in map.Parallax)
        {
            if (cosmetics.NextDouble() >= piece.Probability || piece.Effects.Length == 0)
            {
                continue;
            }

            float m = (float)cosmetics.NextDouble();
            string name = piece.Effects[cosmetics.Next(piece.Effects.Length)];
            var data = assets.Data.Effects[name];
            float Lerp(float[] range) => MathHelper.Lerp(range[0], range[1], (float)cosmetics.NextDouble());
            parallax.Add(
                new()
                {
                    Name = name,
                    Position = new Vector2(Lerp(piece.SpawnRangeX), Lerp(piece.SpawnRangeY)),
                    Velocity = Vector2.Lerp(
                        new(piece.SpeedMin[0], piece.SpeedMin[1]),
                        new(piece.SpeedMax[0], piece.SpeedMax[1]),
                        m
                    ),
                    Z = piece.Depth - m,
                    Scale = piece.DontScale
                        ? new(data.ScaleX, data.ScaleY)
                        : new Vector2(MathHelper.Lerp(.5f, 1.1f, m)),
                    FollowCamera = piece.ParentToCamera,
                    DropFrog = piece.DropFrog,
                }
            );
            return true;
        }

        return false;
    }

    private readonly Dictionary<ParticleEmitterData, SmokeEmitter> smokeEmitters = new();

    private void DrawEmitter(ParticleEmitterData data)
    {
        if (!smokeEmitters.TryGetValue(data, out var emitter))
        {
            int index = activeMap!.ParticleEmitters.OrderBy(e => e.Name, StringComparer.Ordinal).ToList().IndexOf(data);
            smokeEmitters[data] = emitter = new(data, 0x51A71E00u + (uint)index);
        }

        emitter.AdvanceTo(sceneTime);
        var texture = assets.Texture(data.TexturePath);
        assets.SpriteEffect.Parameters["Mode"].SetValue(-1f);
        Batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.NonPremultiplied,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            assets.SpriteEffect
        );
        for (int n = 0; n < emitter.Count; n++)
        {
            if (!emitter.TrySample(n, out var sample))
            {
                continue;
            }

            var location = Screen(new(sample.Position.X, sample.Position.Y));
            float size = sample.Size * cameraController.PixelsPerUnit;
            if (
                location.X + size < 0
                || location.X - size > Width
                || location.Y + size < 0
                || location.Y - size > Height
            )
            {
                continue;
            }

            Batch.Draw(
                texture,
                location,
                null,
                sample.Color,
                -sample.Rotation,
                new Vector2(texture.Width, texture.Height) * .5f,
                new Vector2(size / texture.Width, size / texture.Height),
                SpriteEffects.None,
                0
            );
        }

        canvas.End();
    }

    private void ApplyShimmer(SceneEffectData region)
    {
        if (
            !assets.Data.Materials.TryGetValue(region.Material, out var material)
            || !material.Shader.Contains("Shimmer", StringComparison.Ordinal)
        )
        {
            return;
        }
        var topLeft = Screen(new Vector2(region.X - region.Width * .5f, region.Y + region.Height * .5f));
        var rectangle = new Vector4(
            topLeft.X / Width,
            topLeft.Y / Height,
            region.Width * cameraController.PixelsPerUnit / Width,
            region.Height * cameraController.PixelsPerUnit / Height
        );
        compositor.ApplyShimmer(material, rectangle, time);
    }

    public void DrawPresentation(Texture2D texture) => compositor.DrawPresentation(texture);

    public void Present() => compositor.Present();

    public static Color TeamColor(int team) => PlayerPalette.Team(team);

    public Color ColorFor(World world, int slot) =>
        world.Rules.Lobby ? lobbyColors[slot] : PlayerPalette.For(world, slot);

    public Vector2 Screen(Vector2 world) => cameraController.ToScreen(world);

    public Vector3 ListenerPosition => cameraController.ListenerPosition;

    public void DrawBackdrop(MapData map, float darken = .55f)
    {
        cameraController.Position = new(map.CameraX, map.CameraY);
        cameraController.HalfHeight = map.OrthoSize;
        compositor.BeginScene(EffectSystem.ToColor(map.BackgroundColor));
        canvas.Begin(0);
        foreach (var s in map.Sprites.Where(x => x.Active && x.Visible))
        {
            DrawSceneSprite(s);
        }

        canvas.End();
        PostProcess();
        BeginUi();
        Batch.Draw(assets.White, new Rectangle(0, 0, Width, Height), Color.Black * darken);
        EndUi();
    }

    public void BeginUi()
    {
        compositor.BeginOutput();
        canvas.BeginFont();
    }

    public void EndUi() => Batch.End();

    public void Text(string text, float x, float y, Color? color = null, int scale = 1, bool center = false) =>
        assets.Font.Draw(Batch, text, new(x, y), color ?? Color.White, scale, center);

    public void CenteredText(string text, float x, float y, Color? color = null, int scale = 1, bool center = false) =>
        assets.Font.DrawCenteredVertical(Batch, text, new(x, y), color ?? Color.White, scale, center);

    public void Panel(Rectangle rect, Color color) => Batch.Draw(assets.White, rect, color);

    internal void MenuPanel(Rectangle rect) => menuPanel.Draw(Batch, rect, horizontalPadding: 2);

    internal void ToastPanel(Rectangle rect) => menuPanel.Draw(Batch, rect, alignRight: true);

    private float ImagePixelScale(Rectangle source, float maxWidth)
    {
        float outputScale = compositor.Frame.Width / (float)Width;
        return Math.Max(1, MathF.Floor(maxWidth * outputScale / source.Width));
    }

    internal Vector2 ImageSize(Rectangle source, float maxWidth)
    {
        float outputScale = compositor.Frame.Width / (float)Width;
        float pixelScale = ImagePixelScale(source, maxWidth);
        return new Vector2(source.Width, source.Height) * (pixelScale / outputScale);
    }

    public void Image(string path, Rectangle source, Vector2 topCenter, float maxWidth)
    {
        float outputScale = compositor.Frame.Width / (float)Width;
        float pixelScale = ImagePixelScale(source, maxWidth);
        var position =
            new Vector2(
                MathF.Round(topCenter.X * outputScale - source.Width * pixelScale / 2),
                MathF.Round(topCenter.Y * outputScale)
            ) / outputScale;
        EndUi();
        canvas.Begin(-1);
        Batch.Draw(
            assets.Texture(path),
            position,
            source,
            Color.White,
            0,
            Vector2.Zero,
            pixelScale / outputScale,
            SpriteEffects.None,
            0
        );
        canvas.End();
        BeginUi();
    }

    internal Texture2D Frame => compositor.Frame;

    public void DrawRoundScores(World world, float elapsed)
    {
        var map = assets.Data.PresentationScenes["ScoreScreen"];
        DrawBackdrop(map, 0);
        scores.Draw(world, true, elapsed, time, frameSeconds);
        bool tie =
            world.Match.RoundNumber >= world.Rules.MatchRounds
            && !world.Match.IsShowdown
            && ScoreDisplay
                .Players(world)
                .Count(p => world.Match.Players[p.Slot].TotalScore == world.Match.Players.Max(p => p.TotalScore)) > 1;
        string title =
            world.Rules.Format == MatchFormat.Crews ? "CREW BATTLE COMPLETE"
            : tie && elapsed >= 1 ? "SHOWDOWN ! ! ! "
            : $"ROUND {world.Match.RoundNumber} OF {world.Rules.MatchRounds}";
        BeginUi();
        var header = Screen(new Vector2(0, 11.59f));
        assets.Font.Draw(
            Batch,
            title,
            header,
            tie && elapsed >= 1 && time % .2f >= .1f ? Color.Black : Color.White,
            1,
            true
        );
        assets.Font.Draw(Batch, "raithza.itch.io/frogsmashers", Screen(new Vector2(0, -16.75f)), Color.White, 1, true);
        EndUi();
    }

    private void DrawSceneSprite(SceneSpriteData s)
    {
        if (
            s.Material.Contains("Shimmer", StringComparison.OrdinalIgnoreCase)
            || s.Material.Contains("LocalizedShake", StringComparison.OrdinalIgnoreCase)
        )
        {
            return;
        }

        sceneClocks.TryGetValue(s.SourceId, out var clock);
        if (s.PlayOnce && clock?.Frame >= s.Frames.Length)
        {
            return;
        }

        string sprite =
            s.Frames.Length == 0 || clock == null || clock.Frame < 0
                ? s.SpriteId
                : s.Frames[clock.Frame % s.Frames.Length];
        canvas.DrawSprite(sprite, new(s.X, s.Y), EffectSystem.ToColor(s.Color), new(s.ScaleX, s.ScaleY), s.Rotation);
    }

    private void PostProcess()
    {
        compositor.ComposeScene();
        var data = assets.Data.Effects["LocalizedShake"];
        var sprite = assets.Data.Sprites[data.InitialSpriteId];
        var mask = assets.Texture(sprite.Path);

        foreach (var impact in impacts)
        {
            var size = new Vector2(
                sprite.RectWidth * data.ScaleX / sprite.PixelsPerUnit,
                sprite.RectHeight * data.ScaleY / sprite.PixelsPerUnit
            );
            var corner = Screen(impact.Position + new Vector2(-size.X * sprite.PivotX, size.Y * (1 - sprite.PivotY)));
            var region = new Vector4(
                corner.X / Width,
                corner.Y / Height,
                size.X * cameraController.PixelsPerUnit / Width,
                size.Y * cameraController.PixelsPerUnit / Height
            );
            var offset = new Vector4(impact.Direction, EffectAnimation.PingPong(impact.Age / impact.Life * 2, 1), 0);
            compositor.ApplyImpact(mask, region, offset);
        }
    }

    public void Dispose()
    {
        lobbyOutline?.Dispose();
        lobbyBackground?.Dispose();
        canvas.Dispose();
        compositor.Dispose();
    }

    private sealed class ParallaxPiece
    {
        public string Name = "";
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Scale = Vector2.One;
        public float Z;
        public float Age;
        public bool FollowCamera;
        public bool DropFrog;
        public SpriteAnimation Clock = new();
    }

    private sealed class Impact
    {
        public long Tick;
        public Vector2 Position;
        public Vector2 Direction;
        public float Life;
        public float Age;
    }
}
