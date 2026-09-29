namespace FrogSmashers.Core;

[Flags]
public enum InputButtons : byte
{
    None = 0,
    Jump = 1,
    Attack = 2,
    Tongue = 4,
    Strafe = 8,
}

public readonly record struct InputFrame(sbyte X, sbyte Y, InputButtons Buttons)
{
    public const InputButtons ValidButtons =
        InputButtons.Jump | InputButtons.Attack | InputButtons.Tongue | InputButtons.Strafe;
    public bool Jump => (Buttons & InputButtons.Jump) != 0;
    public bool Attack => (Buttons & InputButtons.Attack) != 0;
    public bool Tongue => (Buttons & InputButtons.Tongue) != 0;
    public bool Strafe => (Buttons & InputButtons.Strafe) != 0;
    public uint Packed => (uint)(byte)X | ((uint)(byte)Y << 8) | ((uint)Buttons << 16);

    public static InputFrame FromPacked(uint packed)
    {
        var result = new InputFrame(
            unchecked((sbyte)packed),
            unchecked((sbyte)(packed >> 8)),
            (InputButtons)(packed >> 16)
        );
        if (
            result.X is < -1 or > 1
            || result.Y is < -1 or > 1
            || (result.Buttons & ~ValidButtons) != 0
            || packed >> 24 != 0
        )
        {
            throw new InvalidDataException("Invalid player input");
        }

        return result;
    }
}
