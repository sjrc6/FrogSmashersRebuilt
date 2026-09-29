using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

public static class EffectAnimation
{
    public static float PingPong(float t, float length) =>
        length - MathF.Abs(t - MathF.Floor(t / (length * 2)) * length * 2 - length);

    public static float TrailScale(float age, float life, float multiplier, bool grow = false) =>
        multiplier * (1 + (grow ? 1 : -1) * age / (life * multiplier));

    public static float TrailAlpha(float age, float life) => 1 - age / life;

    public static float HitScale(float age, float growTime, float scale, float globalTime, float phase) =>
        Math.Min(1, age / growTime) * scale * (1 + MathF.Sin(globalTime * 40 + phase) * .15f);

    public static float ParticleAlpha(float age, float life) =>
        age > life * .75f ? 1 - (age - life * .75f) / life * .25f : 1;

    public static bool Powered(bool hasFly, bool tongue, bool burping) => hasFly && (!tongue || !burping);

    public static Color ScoreFlash(float globalTime, Color player) =>
        globalTime % .15f < .05f ? player
        : globalTime % .15f < .1f ? Color.White
        : Color.Black;

    public static Color SideScoreFlash(float globalTime, Color player) =>
        globalTime % .3f < .1f ? player
        : globalTime % .3f < .2f ? Color.White
        : Color.Black;

    public static float OverheadScoreScale(float timeLeft) => Math.Clamp(1 + (timeLeft - 1.5f) * 2, 1, 1.5f);
}
