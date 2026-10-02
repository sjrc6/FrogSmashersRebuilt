using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed class CinematicPlayer : IDisposable
{
    private readonly Assets assets;
    private readonly SceneAnimationPlayer animation;
    private SceneAnimationPlayer.Visual? victoryFrog;
    private float victoryTime;
    private bool stopVictoryAnimation;
    private readonly Audio? audio;
    private readonly Random cosmeticRandom = new(1733);
    private MapData? scene;
    private string sceneName = "";
    private float elapsed;
    private float cameraPanStarted = -1;
    private float closeupStarted = -1;
    private float shakeTimer;
    private Vector2 shakeCounter;
    private Vector2 shakeSpeed;
    private Vector2 shakeAmount;
    private Vector2 backgroundShake;
    private float lastLoopSound = -1;
    private long epoch;
    private long eventSequence;
    private bool musicStarted;
    public bool TitleReady { get; private set; }
    public bool Finished { get; private set; }
    public bool Active { get; private set; }
    public float Elapsed => elapsed;
    public string SceneName => sceneName;
    public bool ShakeEnabled { get; set; } = true;

    public CinematicPlayer(GraphicsDevice device, Assets assets, Audio? audio = null)
    {
        this.assets = assets;
        this.audio = audio;
        animation = new SceneAnimationPlayer(device, assets, audio);
    }

    public void StartIntro() => Start("TitleScreen", Color.White);

    public void StartOutro(Color color) => Start("Outro", color);

    public void ShowJoin() => Start("JoinScreen", Color.White);

    private void Start(string name, Color color)
    {
        Stop();
        if (!assets.Data.PresentationScenes.TryGetValue(name, out scene))
        {
            return;
        }

        sceneName = name;
        animation.PaletteColor = name == "Outro" ? color : null;
        victoryTime = 0;
        stopVictoryAnimation = false;
        epoch++;
        eventSequence = 0;
        elapsed = 0;
        cameraPanStarted = closeupStarted = -1;
        shakeTimer = 0;
        shakeCounter = shakeSpeed = shakeAmount = backgroundShake = Vector2.Zero;
        lastLoopSound = -1;
        musicStarted = TitleReady = Finished = false;
        Active = true;
        animation.LoadScene(scene);
        victoryFrog = animation.FindVisual("OutroAnim/Foreground/FrogAnimated");
        if (victoryFrog != null)
        {
            victoryFrog.AutomaticAnimation = false;
        }

        foreach (var animator in scene.Animators)
        {
            if (animator.DefaultClip.Length == 0)
            {
                continue;
            }

            if (name == "Outro" && animator.ObjectPath.StartsWith("PartTwo", StringComparison.Ordinal))
            {
                continue;
            }

            animation.PlayClip(animator.ObjectPath, animator.DefaultClip);
        }

        Evaluate();
        animation.PlayInitialSounds();
    }

    public void SkipIntro()
    {
        if (sceneName != "TitleScreen" || !Active)
        {
            return;
        }

        elapsed = 26.2f;
        cameraPanStarted = 20.7f;
        TitleReady = true;
        animation.SkipEvents(elapsed);
        StartMusic();
        Evaluate();
    }

    public void Stop()
    {
        animation.StopAudio();
        Active = false;
    }

    public void Update(float dt)
    {
        if (!Active || scene == null || dt <= 0)
        {
            return;
        }

        elapsed += dt;
        animation.AdvanceAnimations(dt);
        AdvanceVictoryAnimation(dt);
        animation.DispatchEvents(elapsed, Dispatch);
        if (sceneName == "TitleScreen")
        {
            if (elapsed > 20.9f && !TitleReady)
            {
                float loop = MathF.Floor((elapsed - 20.9f) / .4f);
                if (loop > lastLoopSound)
                {
                    lastLoopSound = loop;
                    if (cameraPanStarted < 0 || CameraPhase() < .55f)
                    {
                        PlaySound("TapBat", 1);
                    }
                }
            }

            if (cameraPanStarted >= 0 && CameraPhase() >= 1)
            {
                TitleReady = true;
            }

            if (cameraPanStarted >= 0 && CameraPhase() > .55f)
            {
                shakeTimer -= dt;
                if (shakeTimer <= 0)
                {
                    ShakeBackground();
                    shakeTimer = Range(.2f, 1);
                }
            }
        }

        shakeCounter += shakeSpeed * dt;
        backgroundShake = new Vector2(MathF.Sin(shakeCounter.X), MathF.Sin(shakeCounter.Y)) * shakeAmount;
        shakeAmount = Vector2.Lerp(
            shakeAmount,
            Vector2.Zero,
            Math.Clamp(Parameter("shakeAmountDecay", 1.5f) * dt, 0, 1)
        );
        shakeSpeed = Vector2.Lerp(shakeSpeed, Vector2.Zero, Math.Clamp(Parameter("shakeSpeedDecay", 1) * dt, 0, 1));
        animation.AdvanceParticles(dt);
        Evaluate();
    }

    private float Parameter(string key, float fallback) =>
        scene?.PresentationParameters.GetValueOrDefault(key, fallback) ?? fallback;

    private float Range(float min, float max) => MathHelper.Lerp(min, max, (float)cosmeticRandom.NextDouble());

    private float CameraPhase() =>
        Math.Clamp((elapsed - cameraPanStarted - Parameter("cameraPanDelay", 1.5f)) / Parameter("panTime", 4), 0, 1);

    private void ShakeBackground()
    {
        int level = cosmeticRandom.Next(1, 4);
        PlayBackgroundSound("BatHit" + level, Range(.2f, .5f));
        if (cosmeticRandom.NextDouble() < .5)
        {
            PlayBackgroundSound("BatHitVoice" + level, Range(.2f, .5f));
        }
        else if (cosmeticRandom.NextDouble() < .25)
        {
            PlayBackgroundSound("LaunchVoice3", Range(.2f, .5f));
        }

        if (cosmeticRandom.NextDouble() < .5)
        {
            float maxSpeed = Parameter("shakeMaxSpeed", 40);
            float maxAmount = Parameter("shakeMaxAmount", .15f);
            shakeSpeed = Vector2.Min(
                shakeSpeed + new Vector2(Range(0, maxSpeed), Range(0, maxSpeed)),
                new Vector2(maxSpeed)
            );
            shakeAmount = Vector2.Min(
                shakeAmount + new Vector2(Range(0, maxAmount), Range(0, maxAmount)),
                new Vector2(maxAmount)
            );
        }

        if (cosmeticRandom.NextDouble() < .5 && scene!.IntroParticleEffects.Length > 0)
        {
            string effect = scene.IntroParticleEffects[cosmeticRandom.Next(scene.IntroParticleEffects.Length)];
            var c = assets.Data.EffectsColors[cosmeticRandom.Next(assets.Data.EffectsColors.Length)];
            float angle = MathHelper.ToRadians(Range(-45, 45));
            float speed = Parameter("characterParticleLaunchVelocity", 15);
            animation.AddParticle(
                new Vector2(Range(-5, 5), -2),
                new Vector2(-MathF.Sin(angle), MathF.Cos(angle)) * speed,
                effect,
                PackColor(new Vector4(c[0], c[1], c[2], c[3]))
            );
        }
    }

    private void Dispatch(TimelineEventData evt, float time)
    {
        switch (evt.Function)
        {
            case "PlaySound":
                PlaySound(evt.Argument ?? "", 1);
                break;
            case "StartCameraPan":
                cameraPanStarted = time;
                break;
            case "StartMusic":
                StartMusic();
                break;
            case "StopFrogAnimating":
                stopVictoryAnimation = true;
                break;
            case "SwitchToCloseup":
                if (closeupStarted >= 0)
                {
                    break;
                }

                closeupStarted = time;
                var binding = scene!.Animators.FirstOrDefault(a =>
                    a.ObjectPath.StartsWith("PartTwo", StringComparison.Ordinal)
                );
                if (binding != null)
                {
                    animation.PlayClip(binding.ObjectPath, binding.DefaultClip, time);
                }

                break;
            case "Finished":
                Finished = true;
                break;
        }
    }

    private void PlaySound(string name, float volume)
    {
        if (audio == null || name.Length == 0)
        {
            return;
        }

        string? key = assets.Data.Sounds.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (key != null)
        {
            audio.Play(key, long.MinValue + (epoch << 24) + ++eventSequence, volume);
        }
    }

    private void PlayBackgroundSound(string name, float volume)
    {
        if (audio == null)
        {
            return;
        }

        audio.ListenerPosition = new Vector3(animation.Camera, -10);
        audio.PlayAt(
            name,
            long.MinValue + (epoch << 24) + ++eventSequence,
            volume,
            new Vector3(animation.Camera + new Vector2(Range(-5, 5), 0), -10)
        );
    }

    private void StartMusic()
    {
        if (musicStarted)
        {
            return;
        }

        musicStarted = true;
        animation.PlayDeferredSounds();
    }

    private void AdvanceVictoryAnimation(float dt)
    {
        if (
            victoryFrog == null
            || victoryFrog.Destroyed
            || !victoryFrog.AnimationEnabled
            || !victoryFrog.Transform.WorldActive
            || victoryFrog.Source.Frames.Length == 0
        )
        {
            return;
        }

        victoryTime += dt;
        float phase = victoryTime % 2;
        victoryFrog.Frame =
            phase < .9f ? 3
            : phase < 1 ? 2
            : phase < 1.9f ? 1
            : 2;
        if (phase < .9f && stopVictoryAnimation)
        {
            victoryTime -= dt;
        }
    }

    private void Evaluate()
    {
        if (scene == null)
        {
            return;
        }

        animation.ResetPose();
        if (closeupStarted >= 0 && animation.FindTransform("PartTwo") is { } closeup)
        {
            closeup.Active = true;
        }

        animation.ApplyClips(elapsed);
        if (sceneName == "TitleScreen" && elapsed > 20.9f)
        {
            animation.ApplyLoop("IntroAnim", "IntroEndLoop", elapsed - 20.9f);
        }

        if (sceneName == "TitleScreen" && cameraPanStarted >= 0)
        {
            float phase = CameraPhase();
            Span<float> sample = stackalloc float[4];
            float pan =
                scene.CameraPanCurve.Count > 0 ? AnimationCurve.Sample(scene.CameraPanCurve, phase, sample)[0] : phase;
            animation.Camera = new Vector2(scene.CameraX, scene.CameraY + pan * Parameter("panTo", 6));
            if (animation.FindTransform("Main Camera") is { } cameraNode)
            {
                cameraNode.Position.Y = animation.Camera.Y;
            }

            if (ShakeEnabled && animation.FindTransform("bg") is { } background)
            {
                background.Position += new Vector3(backgroundShake, 0);
            }
        }
        else
        {
            animation.Camera = new Vector2(scene.CameraX, scene.CameraY);
        }

        animation.ResolvePose();
    }

    public Texture2D Draw() => animation.Draw(Active);

    private static Color PackColor(Vector4 color) =>
        new(
            (byte)Math.Clamp(MathF.Round(color.X * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(color.Y * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(color.Z * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(color.W * 255), 0, 255)
        );

    public void Dispose()
    {
        Stop();
        animation.Dispose();
    }
}
