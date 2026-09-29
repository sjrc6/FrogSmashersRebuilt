using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using static FrogSmashers.Client.EffectSystem;

namespace FrogSmashers.Client;

internal sealed class EffectTemplate(string name, EffectData data)
{
    public EffectData Data { get; } = data;
    public Color[] Colors { get; } = data.Colors.Select(ToColor).ToArray();
    public VisualEffectKind Kind { get; } =
        data.Parameters.ContainsKey("growTime")
            ? VisualEffectKind.Hit
            : name switch
            {
                "HitParticle" => VisualEffectKind.Star,
                "SideScorePlum" => VisualEffectKind.ScorePlume,
                "KnockedUpEffect" => VisualEffectKind.FallingFrog,
                _ => VisualEffectKind.Sprite,
            };
    public float Depth { get; } =
        name switch
        {
            "HitEffect" => 2,
            "HitStar" => 3,
            "HitStarPowerHit" => 4,
            "SmokeRing" => -3,
            "KnockedUpEffect" => -5,
            "SpawnPuff" or "TongueHitEffect" or "ShingEffect" or "Confetti." => -1,
            "HitParticle" => 0,
            _ => 1,
        };
    public float Lifetime { get; } = data.Parameters.GetValueOrDefault("life", .25f);
    public float StartSize { get; } = data.Parameters.GetValueOrDefault("startSize", .2f);
    public float EndSize { get; } = data.Parameters.GetValueOrDefault("endSize", .05f);
    public float AccelerationX { get; } = data.Parameters.GetValueOrDefault("accelX", 0);
    public float AccelerationY { get; } = data.Parameters.GetValueOrDefault("accelY", 0);
    public float RotationInterval { get; } = data.Parameters.GetValueOrDefault("rotationDelay", .075f);
    public float ColorInterval { get; } = data.Parameters.GetValueOrDefault("colorChangeDelay", .05f);
    public float RotationSpeed { get; } = data.Parameters.GetValueOrDefault("rotSpeed", 270);
    public float BodyDuration { get; } = data.Parameters.GetValueOrDefault("animTime", .1f);
    public float TextFadeDuration { get; } = data.Parameters.GetValueOrDefault("textFadeTime", 1.5f);
    public float GrowthDuration { get; } = data.Parameters.GetValueOrDefault("growTime", .1f);
    public float FlipColors { get; } = data.Parameters.GetValueOrDefault("flipColors", 0);
    public float ScalePhase { get; } = data.Parameters.GetValueOrDefault("scaleCounter", 0);
    public float DeathFrameInterval { get; } = data.Parameters.GetValueOrDefault("deathFrameRate", .04f);
    public float FadeAlpha { get; } = data.Parameters.GetValueOrDefault("alphaOut", 0);
    public float FallDelay { get; } = data.Parameters.GetValueOrDefault("fallDelay", .5f);
    public float FallSpeed { get; } = data.Parameters.GetValueOrDefault("fallSpeed", 30);
    public float FrameInterval { get; } = data.Parameters.GetValueOrDefault("frameDelay", .05f);
}
