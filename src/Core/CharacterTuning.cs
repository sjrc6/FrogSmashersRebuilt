namespace FrogSmashers.Core;

internal sealed class CharacterTuning
{
    private readonly Dictionary<string, Fixed> parameters;
    public Fixed MaxRunSpeed { get; }
    public Fixed RunAccel { get; }
    public Fixed JumpVel { get; }
    public Fixed GravityGraceTime { get; }
    public Fixed BounceAccel { get; }
    public Fixed BounceDodgePower { get; }
    public Fixed TongueRetractSpeedLatched { get; }
    public Fixed BounceGravityMin { get; }
    public Fixed BounceGravityRestoreDelay { get; }
    public Fixed BounceGravityMax { get; }
    public Fixed BounceGravityRestoreTime { get; }
    public Fixed MaxFallSpeed { get; }
    public Fixed SkidAccel { get; }
    public Fixed Gravity { get; }
    public Fixed GravityGraceThreshold { get; }
    public Fixed AttackChargeTime { get; }
    public Fixed AttackTime { get; }
    public Fixed AttackRange { get; }
    public Fixed AttackRecoverTime { get; }
    public Fixed TongueDelay { get; }
    public Fixed TongueSpeed { get; }
    public Fixed MinimumTongueDistance { get; }
    public Fixed TongueRange { get; }
    public Fixed JumpGraceTime { get; }
    public Fixed AirAccel { get; }
    public Fixed MaxFallSpeedWallSlide { get; }
    public Fixed TongueRetractSpeedMissed { get; }

    public CharacterTuning(Dictionary<string, decimal>? source)
    {
        parameters = source?.ToDictionary(pair => pair.Key, pair => Fixed.FromDecimal(pair.Value)) ?? [];
        MaxRunSpeed = Read("maxRunSpeed", 20);
        RunAccel = Read("runAccel", 175);
        JumpVel = Read("jumpVel", 40);
        GravityGraceTime = Read("gravityGraceTime", .25m);
        BounceAccel = Read("bounceAccel", 20);
        BounceDodgePower = Read("bounceDodgePower", 20);
        TongueRetractSpeedLatched = Read("tongueRetractSpeedLatched", 25);
        BounceGravityMin = Read("bounceGravityMin", 20);
        BounceGravityRestoreDelay = Read("bounceGravityRestoreDelay", 1);
        BounceGravityMax = Read("bounceGravityMax", 50);
        BounceGravityRestoreTime = Read("bounceGravityRestoreTime", 1.5m);
        MaxFallSpeed = Read("maxFallSpeed", -40);
        SkidAccel = Read("skidAccel", 100);
        Gravity = Read("gravity", 200);
        GravityGraceThreshold = Read("gravityGraceThreshold", 15);
        AttackChargeTime = Read("attackChargeTime", 1.25m);
        AttackTime = Read("attackTime", .01m);
        AttackRange = Read("attackRange", 3);
        AttackRecoverTime = Read("attackRecoverTime", .35m);
        TongueDelay = Read("tongueDelay", .05m);
        TongueSpeed = Read("tongueSpeed", 60);
        MinimumTongueDistance = Read("minimumTongueDistance", 3);
        TongueRange = Read("tongueRange", 10);
        JumpGraceTime = Read("jumpGraceTime", .2m);
        AirAccel = Read("airAccel", 200);
        MaxFallSpeedWallSlide = Read("maxFallSpeedWallSlide", -10);
        TongueRetractSpeedMissed = Read("tongueRetractSpeedMissed", 27.5m);
    }

    private Fixed Read(string name, decimal fallback)
    {
        return parameters.TryGetValue(name, out var value) ? value : Fixed.FromDecimal(fallback);
    }

    public void WriteConfiguration(BinaryWriter writer)
    {
        writer.Write(parameters.Count);
        foreach (var parameter in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(parameter.Key);
            writer.Write(parameter.Value.Raw);
        }
    }
}
