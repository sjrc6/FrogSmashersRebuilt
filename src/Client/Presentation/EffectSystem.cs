using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class EffectSystem
{
    private readonly Assets assets;
    private readonly SpriteCanvas canvas;
    private readonly GameCamera cameraController;
    private readonly Random cosmetics;
    private readonly Func<World, int, Color> colorFor;
    private readonly Dictionary<string, EffectTemplate> templates;
    private readonly Dictionary<EffectData, int> renderModes;
    private readonly List<VisualEffect> effects = new();
    private float time;
    public IReadOnlyList<VisualEffect> Active => effects;

    public EffectSystem(
        Assets assets,
        SpriteCanvas canvas,
        GameCamera camera,
        Random random,
        Func<World, int, Color> colorFor
    )
    {
        this.assets = assets;
        this.canvas = canvas;
        cameraController = camera;
        cosmetics = random;
        this.colorFor = colorFor;
        renderModes = assets
            .Data.Effects.Values.Append(assets.Data.TongueTip)
            .Distinct()
            .ToDictionary(data => data, data => ResolveRenderMode(assets, data));
        templates = assets.Data.Effects.ToDictionary(
            entry => entry.Key,
            entry => new EffectTemplate(entry.Key, entry.Value)
        );
    }

    public void Reset() => effects.Clear();

    public void Rewind(long fromTick) => effects.RemoveAll(effect => effect.Tick >= fromTick);

    public void RemoveCharacter(int player) => effects.RemoveAll(effect => effect.Owner == player);

    public void Update(float dt, float elapsed)
    {
        time = elapsed;
        int count = effects.Count;
        for (int index = 0; index < count; index++)
        {
            UpdateEffect(effects[index], dt, elapsed);
        }
        effects.RemoveAll(effect => effect.Dead);
    }

    private float CharacterParameter(string name, float fallback) =>
        assets.Data.CharacterVisualParameters.GetValueOrDefault(name, fallback);

    private float RandomRange(float low, float high) => MathHelper.Lerp(low, high, (float)cosmetics.NextDouble());

    private Vector2 InsideCircle()
    {
        float angle = RandomRange(0, MathF.Tau);
        float r = MathF.Sqrt(RandomRange(0, 1));
        return new(MathF.Cos(angle) * r, MathF.Sin(angle) * r);
    }

    internal static float Noise(uint value)
    {
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return (value & 0xffffff) / 16777216f;
    }

    internal static Color ToColor(float[] value) =>
        new(
            (int)MathF.Round(value[0] * 255),
            (int)MathF.Round(value[1] * 255),
            (int)MathF.Round(value[2] * 255),
            (int)MathF.Round((value.Length > 3 ? value[3] : 1) * 255)
        );

    private static Color Alpha(Color color, float alpha)
    {
        color.A = (byte)MathF.Round(Math.Clamp(alpha, 0, 1) * 255);
        return color;
    }

    public int ModeFor(EffectData data) => renderModes.GetValueOrDefault(data);

    private static int ResolveRenderMode(Assets assets, EffectData data)
    {
        if (!assets.Data.Materials.TryGetValue(data.Material, out var material))
        {
            return 0;
        }
        return material.Shader switch
        {
            "SelectiveColorReplace" => 1,
            "Trail" => 2,
            "ColorCutout" => 3,
            _ => 0,
        };
    }

    public VisualEffect? Add(string name, Vector2 position, Color color, long tick, EffectSpawn? options = null)
    {
        var spawn = options ?? new EffectSpawn();
        if (!templates.TryGetValue(name, out var template) || template.Data.Frames.Length == 0)
        {
            return null;
        }

        var data = template.Data;
        var effect = new VisualEffect
        {
            Name = name,
            Kind = template.Kind,
            Template = template,
            Position = position,
            Color = color,
            BaseColor = color,
            Tick = tick,
            Scale = spawn.Scale,
            CurrentScale = new Vector2(data.ScaleX * spawn.Facing, data.ScaleY) * spawn.Stretch,
            Velocity = spawn.Velocity,
            Rotation = spawn.Rotation,
            Facing = spawn.Facing,
            Seed = (uint)cosmetics.Next(),
            Sprite = data.InitialSpriteId,
            FrameDelay = data.FrameSeconds,
            Life = spawn.Life > 0 ? spawn.Life : template.Lifetime,
            StartSize = template.StartSize,
            EndSize = template.EndSize,
            Colors = template.Colors,
            Acceleration = new Vector2(template.AccelerationX, template.AccelerationY),
            Z = template.Depth,
        };
        if (string.IsNullOrEmpty(effect.Sprite))
        {
            effect.Sprite = data.Frames[0];
        }

        effect.RotationCounter = RandomRange(0, template.RotationInterval);
        effect.ColorCounter = name == "HitParticle" ? RandomRange(0, template.ColorInterval) : 0;
        if (name == "KnockedUpEffect")
        {
            effect.Spin = MathHelper.ToRadians(template.RotationSpeed) * RandomRange(-2, 2);
        }

        if (name == "SideScorePlum")
        {
            effect.Z = 1;
            effect.Life = template.BodyDuration + template.TextFadeDuration;
            effect.TextScale = 0;
        }

        if (effect.Kind == VisualEffectKind.ScorePlume)
        {
            effect.Points = spawn.Points;
            effect.Text = spawn.Points != 0 ? spawn.Points.ToString("+0;-0;0") : DeathQuip(effect.Seed);
            effect.TextColor = color;
            effect.TextColorFloat = color.ToVector4();
            effect.CameraRelative = spawn.CameraRelative;
        }
        effects.Add(effect);
        if (spawn.Age > 0)
        {
            float left = spawn.Age;
            while (left > 0 && !effect.Dead)
            {
                float dt = Math.Min(left, 1 / 60f);
                UpdateEffect(effect, dt, time - left + dt);
                left -= dt;
            }
        }

        return effect;
    }

    private void UpdateEffect(VisualEffect effect, float dt, float globalTime)
    {
        if (effect.Dead)
        {
            return;
        }

        effect.Age += dt;
        if (effect.Owner >= 0)
        {
            effect.Color = Alpha(effect.BaseColor, EffectAnimation.TrailAlpha(effect.Age, .3f));
            float scale = EffectAnimation.TrailScale(effect.Age, .3f, effect.Scale);
            effect.CurrentScale = new(scale * effect.Facing, scale);
            effect.Z = 1 - effect.Age * .1f;
            effect.Dead = scale < 1 || effect.Age > .3f * effect.Scale;
            return;
        }

        var template = effect.Template!;
        var data = template.Data;
        bool hit = effect.Kind == VisualEffectKind.Hit;
        bool star = effect.Kind == VisualEffectKind.Star;
        if (hit)
        {
            effect.ColorCounter += dt;
            effect.RotationCounter += dt;
            if (effect.Age < effect.Life)
            {
                if (template.FlipColors != 0 && effect.ColorCounter >= .04f)
                {
                    effect.ColorIndex++;
                    effect.ColorCounter -= .04f;
                    effect.Color = effect.Colors[effect.ColorIndex % effect.Colors.Length];
                }

                float scale = EffectAnimation.HitScale(
                    effect.Age,
                    template.GrowthDuration,
                    effect.Scale,
                    globalTime,
                    template.ScalePhase
                );
                effect.CurrentScale = new(scale);
                if (effect.RotationCounter >= template.RotationInterval)
                {
                    effect.RotationCounter -= template.RotationInterval;
                    effect.Rotation = RandomRange(0, MathF.Tau);
                }
            }
            else
            {
                effect.DeathCounter -= dt;
                if (effect.DeathCounter < 0)
                {
                    effect.DeathCounter = template.DeathFrameInterval;
                    effect.DeathFrame++;
                    if (
                        !data.Animations.TryGetValue("deathFrames", out var frames)
                        || effect.DeathFrame >= frames.Length
                    )
                    {
                        effect.Dead = true;
                    }
                    else
                    {
                        effect.Sprite = frames[effect.DeathFrame];
                    }
                }
            }
        }
        else if (star)
        {
            effect.ColorCounter += dt;
            effect.CurrentScale = new(
                MathHelper.Lerp(effect.StartSize, effect.EndSize, Math.Clamp(effect.Age / effect.Life, 0, 1))
            );
            if (effect.ColorCounter >= template.ColorInterval)
            {
                effect.ColorCounter -= template.ColorInterval;
                effect.ColorIndex++;
                effect.Color = effect.Colors[effect.ColorIndex % effect.Colors.Length];
            }

            if (template.FadeAlpha != 0)
            {
                effect.Color = Alpha(effect.Color, EffectAnimation.ParticleAlpha(effect.Age, effect.Life));
            }

            effect.Position += effect.Velocity * dt;
            if (effect.Age > effect.Life)
            {
                effect.Dead = true;
            }
        }
        else if (effect.Kind == VisualEffectKind.ScorePlume)
        {
            UpdateSideScore(effect, dt, globalTime);
        }
        else
        {
            if (effect.Kind == VisualEffectKind.FallingFrog)
            {
                if (effect.Age >= template.FallDelay)
                {
                    effect.Position.Y -= template.FallSpeed * dt;
                    effect.Rotation += effect.Spin * dt;
                }

                if (
                    effect.Position.Y < 0
                    && !canvas.SpriteVisible(effect.Sprite, effect.Position, effect.CurrentScale, effect.Rotation)
                )
                {
                    effect.Dead = true;
                }
            }
            else
            {
                effect.Position += effect.Velocity * dt;
                effect.Velocity += effect.Acceleration * dt;
            }

            if (effect.Clock.Step(dt, effect.FrameDelay, data.Frames.Length, data.PlayOnce))
            {
                effect.Dead = true;
            }

            if (effect.Clock.Frame >= 0)
            {
                effect.Sprite = data.Frames[effect.Clock.Frame % data.Frames.Length];
            }

            if (
                effect.Age > 10
                && !data.PlayOnce
                && !canvas.SpriteVisible(effect.Sprite, effect.Position, effect.CurrentScale, effect.Rotation)
            )
            {
                effect.Dead = true;
            }
        }
    }

    private void UpdateSideScore(VisualEffect effect, float dt, float globalTime)
    {
        var template = effect.Template!;
        var data = template.Data;
        float body = template.BodyDuration;
        float fade = template.TextFadeDuration;
        float delay = template.FrameInterval;
        bool wasLooping = effect.Age - dt <= body;
        effect.Clock.Counter += dt;
        if (effect.Age > body && wasLooping)
        {
            effect.Clock.Frame = -1;
            effect.Clock.Counter = delay;
        }

        if (effect.Clock.Counter >= delay)
        {
            effect.Clock.Counter -= delay;
            effect.Clock.Frame++;
            var frames = data.Animations[effect.Age <= body ? "loop" : "deathAnim"];
            effect.Sprite =
                effect.Age <= body ? frames[effect.Clock.Frame % frames.Length]
                : effect.Clock.Frame < frames.Length ? frames[effect.Clock.Frame]
                : null;
            if (effect.Sprite != null && effect.Points > 0)
            {
                SprayParticles(
                    effect.Position + (effect.CameraRelative ? cameraController.Position : Vector2.Zero),
                    new(MathF.Cos(effect.Rotation), MathF.Sin(effect.Rotation)),
                    15 + 5 * effect.Points,
                    effect.Points,
                    .22f,
                    15,
                    effect.Tick
                );
            }
        }

        float m = (effect.Age - body) / fade;
        effect.TextScale = MathHelper.Lerp(
            effect.TextScale,
            effect.Age <= body || m < .5f ? 1 : 1.5f,
            Math.Clamp(
                dt
                    * (
                        effect.Age <= body ? 10
                        : m < .5f ? 2
                        : 1
                    ),
                0,
                1
            )
        );
        effect.TextDistance = MathHelper.Lerp(
            effect.TextDistance,
            4,
            Math.Clamp(dt * (effect.Age <= body ? 5 : 3), 0, 1)
        );
        Color target = EffectAnimation.SideScoreFlash(globalTime, effect.BaseColor);
        float a = effect.TextColorFloat.W;
        effect.TextColorFloat = Vector4.Lerp(effect.TextColorFloat, target.ToVector4(), Math.Clamp(dt * 25, 0, 1));
        if (effect.Age > body && m > .5f)
        {
            a = MathHelper.Lerp(1, 0, Math.Clamp((m - .5f) * 2, 0, 1));
        }

        effect.TextColorFloat.W = a;
        effect.TextColor = ToColor([
            effect.TextColorFloat.X,
            effect.TextColorFloat.Y,
            effect.TextColorFloat.Z,
            effect.TextColorFloat.W,
        ]);
        effect.Dead = effect.Age > body + fade;
    }

    private void SprayParticles(
        Vector2 position,
        Vector2 direction,
        float force,
        int amount,
        float life,
        float angle,
        long tick
    )
    {
        for (int i = 0; i < amount; i++)
        {
            float rotation = MathHelper.ToRadians(MathHelper.Lerp(-angle, angle, (i + 1f) / (amount + 2f)));
            var velocity = Vector2.Transform(direction, Matrix.CreateRotationZ(rotation)) * force;
            Add(
                "HitParticle",
                position + InsideCircle(),
                Color.White,
                tick,
                new EffectSpawn { Velocity = velocity, Life = life * RandomRange(.9f, 1.1f) }
            );
        }
    }

    public void UpdateCharacter(
        PlayerState player,
        World world,
        Vector2 position,
        CharacterPresentation animator,
        float dt
    )
    {
        float localDt = dt * (player.HitstopTicks > 0 ? player.HitstopScale.ToFloat() : 1);
        float delay = CharacterParameter("trailDelay", .01f);
        var visual = new FlightVisual
        {
            Owner = player.Slot,
            Tick = world.TickNumber,
            Hits = player.HitsTaken,
            Center = position + Vector2.UnitY,
            Velocity = new(player.VX.ToFloat(), player.VY.ToFloat()),
            SpritePosition = position + new Vector2(assets.Data.CharacterOffsetX, animator.OffsetY),
            Sprite = animator.Sprite,
            Rotation = animator.Rotation,
            Facing = player.Facing,
            Color = colorFor(world, player.Slot),
            AttackerColor = player.LastHitBy >= 0 ? colorFor(world, player.LastHitBy) : null,
            Airborne = player.Mode == CharacterMode.Bouncing && !player.OnGround,
            Silhouette =
                player.HitsTaken > 0
                && (
                    player.Mode == CharacterMode.Bouncing
                    || player.Mode == CharacterMode.Tongue && player.WasBouncingBeforeTongue
                ),
            Frozen = player.HitstopTicks > 0 && player.HitstopScale == 0,
            Dodged = player.HasBounceDodged,
            Recovered = player.CanBounceDodge,
        };
        UpdateFlight(visual, animator.Flight, dt, localDt);

        if (
            player.Mode == CharacterMode.Bouncing
            && player.OnGround
            && MathF.Abs(position.X - animator.LastSkidX) > CharacterParameter("skidEffectDistance", 1)
            && MathF.Abs(player.VX.ToFloat()) > 20
        )
        {
            animator.LastSkidX = position.X;
            Add("JumpPuffSkew", position, Color.White, world.TickNumber, new EffectSpawn { Facing = player.Facing });
        }

        if (
            EffectAnimation.Powered(
                player.HasFly,
                player.Mode == CharacterMode.Tongue,
                player.TonguePhase == TonguePhase.Burping
            )
        )
        {
            animator.Flight.TrailFaderCounter -= dt;
            if (animator.Flight.TrailFaderCounter <= 0)
            {
                animator.Flight.TrailNumber++;
                animator.Flight.TrailFaderCounter += delay * 7;
                animator.Color =
                    animator.Flight.TrailNumber % 3 == 0 ? Color.White
                    : animator.Flight.TrailNumber % 3 == 1 ? Color.Black
                    : colorFor(world, player.Slot);
            }
        }
        else
        {
            animator.Color = colorFor(world, player.Slot);
        }
    }

    private void UpdateFlight(FlightVisual visual, FlightPresentation trail, float dt, float localDt)
    {
        float delay = CharacterParameter("trailDelay", .01f);
        float speed = visual.Velocity.Length();
        var center = visual.Center;
        var velocity = visual.Velocity;
        if (visual.Airborne && !visual.Frozen && !visual.Dodged && !visual.Recovered && visual.Hits > 2)
        {
            trail.ParticleCounter += localDt;
            if (trail.ParticleCounter > delay)
            {
                trail.ParticleCounter -= delay;
                var effect = Add(
                    "HitParticle",
                    center + InsideCircle(),
                    Color.White,
                    visual.Tick,
                    new EffectSpawn
                    {
                        Velocity = velocity * .1f * RandomRange(.75f, 1.25f),
                        Life = .5f * RandomRange(.9f, 1.1f),
                    }
                );
                if (effect != null)
                {
                    effect.StartSize = .15f;
                    effect.EndSize = 0;
                    effect.Colors = [Color.White, Color.Black, visual.Color];
                }
            }
        }

        if (visual.Silhouette && visual.AttackerColor != null)
        {
            trail.TrailFaderCounter -= dt;
            if (trail.TrailFaderCounter <= 0)
            {
                trail.TrailNumber++;
                trail.TrailFaderCounter += delay * 2;
                var tint = Color.Lerp(
                    visual.AttackerColor!.Value,
                    Color.White,
                    EffectAnimation.PingPong(trail.TrailNumber * .2f, 1)
                );
                effects.Add(
                    new()
                    {
                        Tick = visual.Tick,
                        Name = "FaderTrail",
                        Owner = visual.Owner,
                        Sprite = visual.Sprite,
                        SquareSize = visual.SquareSize,
                        Color = Alpha(tint, .5f),
                        BaseColor = tint,
                        Scale = 1.1f + visual.Hits * .15f,
                        Position = visual.SpritePosition,
                        Rotation = visual.Rotation,
                        Facing = visual.Facing,
                        CurrentScale = new(visual.Facing, 1),
                        Z = 1,
                    }
                );
            }
        }

        if (visual.Airborne && visual.Hits > 0 && !visual.Dodged)
        {
            if (
                Vector2.Distance(trail.LastSmoke, center) > CharacterParameter("smokeRingDistance", 7.5f)
                && speed > 20
                && visual.Hits > 2
            )
            {
                trail.LastSmoke = center;
                var spawn = new EffectSpawn { Rotation = visual.Rotation };
                Add("SmokeRing", center, Color.White, visual.Tick, spawn);
                Add("SmokeRingBack", center, Color.White, visual.Tick, spawn);
            }

            trail.TrailCounter -= localDt;
            if (trail.TrailCounter < 0)
            {
                trail.TrailCounter += delay;
                if (visual.AttackerColor != null)
                {
                    for (int i = 0; i < visual.Hits; i++)
                    {
                        bool dark = RandomRange(0, 1) < .5f;
                        var tint = Color.Lerp(
                            visual.Color,
                            dark ? Color.Black : Color.White,
                            RandomRange(0, dark ? .3f : .8f)
                        );
                        if (speed > 20)
                        {
                            var effect = Add(
                                "LineParticle",
                                center + InsideCircle() * .5f,
                                tint,
                                visual.Tick,
                                new EffectSpawn
                                {
                                    Rotation = visual.Rotation,
                                    Stretch = new(1, .3f + speed * CharacterParameter("lineVelocityScale", .005f)),
                                }
                            );
                            if (effect != null)
                            {
                                effect.FrameDelay *= (.25f + .5f * speed / 20) * RandomRange(.9f, 1.2f);
                            }
                        }
                    }
                }
            }
        }

        foreach (var effect in effects.Where(effect => effect.Owner == visual.Owner))
        {
            effect.Position = visual.SpritePosition;
            effect.Sprite = visual.Sprite;
            effect.Rotation = visual.Rotation;
            effect.Facing = visual.Facing;
            effect.CurrentScale.X = MathF.Abs(effect.CurrentScale.X) * visual.Facing;
        }
    }

    public void UpdateBeachBall(World world, Vector2 position, FlightPresentation trail, float dt)
    {
        var ball = world.BeachBall;
        var visual = new FlightVisual
        {
            Owner = 8,
            Tick = world.TickNumber,
            Hits = ball.HitsTaken,
            Center = position,
            Velocity = new(ball.VX.ToFloat(), ball.VY.ToFloat()),
            SpritePosition = position,
            Facing = 1,
            Rotation = MathF.Atan2(ball.VY.ToFloat(), ball.VX.ToFloat()) - MathF.PI / 2,
            Color = Color.White,
            AttackerColor = ball.LastHitBy >= 0 ? colorFor(world, ball.LastHitBy) : null,
            Airborne = !ball.Resting,
            Silhouette = ball.HitsTaken > 0,
            Frozen = ball.HitstopTicks > 0,
            Recovered = ball.Settling,
            SquareSize = BeachBallState.Radius.ToFloat() * 2,
        };
        UpdateFlight(visual, trail, dt, ball.HitstopTicks > 0 ? 0 : dt);
    }

    internal enum VisualEffectKind
    {
        Sprite,
        Hit,
        Star,
        ScorePlume,
        FallingFrog,
    }

    private static string DeathQuip(uint seed)
    {
        string[] words =
        [
            "OOPS!",
            "DERP!",
            "FAIL!",
            "CRAP!",
            "SHIT!",
            "CRUD!",
            ":(",
            "DARN!",
            "BUTTS!",
            "FAIL!",
            "DRAT!",
            "OH DEAR!",
            "OH NO!",
            "FAREWELL, CRUEL WORLD!",
        ];
        for (int i = 0; i < words.Length; i++)
        {
            if (Noise(seed + (uint)i) < .2f)
            {
                return words[i];
            }
        }

        return "~_~";
    }

    internal sealed class VisualEffect
    {
        public VisualEffectKind Kind;
        public string Name = "";
        public string Text = "";
        public string? Sprite;
        public EffectTemplate? Template;
        public EffectData? Data => Template?.Data;
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Acceleration;
        public Vector2 CurrentScale = Vector2.One;
        public Color Color;
        public Color BaseColor;
        public Color TextColor = Color.White;
        public Vector4 TextColorFloat = Vector4.One;
        public Color[] Colors = [];
        public long Tick;
        public float Age;
        public float Life;
        public float Scale = 1;
        public float Rotation;
        public float Spin;
        public float Z;
        public float StartSize;
        public float EndSize;
        public float ColorCounter;
        public float RotationCounter;
        public float DeathCounter;
        public float FrameDelay;
        public float TextDistance;
        public float TextScale;
        public int Queue = -1;
        public int Facing = 1;
        public int Points;
        public int Owner = -1;
        public float SquareSize;
        public int ColorIndex;
        public int DeathFrame = -1;
        public uint Seed;
        public bool Dead;
        public bool CameraRelative;
        public SpriteAnimation Clock = new();
    }
}
