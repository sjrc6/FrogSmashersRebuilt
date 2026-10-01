using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed class PadBindings
{
    internal static readonly Buttons[] BindableButtons =
    [
        Buttons.A,
        Buttons.B,
        Buttons.X,
        Buttons.Y,
        Buttons.LeftShoulder,
        Buttons.RightShoulder,
        Buttons.LeftTrigger,
        Buttons.RightTrigger,
        Buttons.LeftStick,
        Buttons.RightStick,
        Buttons.Back,
        Buttons.DPadLeft,
        Buttons.DPadRight,
        Buttons.DPadUp,
        Buttons.DPadDown,
        Buttons.LeftThumbstickLeft,
        Buttons.LeftThumbstickRight,
        Buttons.LeftThumbstickUp,
        Buttons.LeftThumbstickDown,
        Buttons.RightThumbstickLeft,
        Buttons.RightThumbstickRight,
        Buttons.RightThumbstickUp,
        Buttons.RightThumbstickDown,
    ];

    public Buttons Left { get; set; } = Buttons.DPadLeft | Buttons.LeftThumbstickLeft;
    public Buttons Right { get; set; } = Buttons.DPadRight | Buttons.LeftThumbstickRight;
    public Buttons Up { get; set; } = Buttons.DPadUp | Buttons.LeftThumbstickUp;
    public Buttons Down { get; set; } = Buttons.DPadDown | Buttons.LeftThumbstickDown;
    public Buttons Jump { get; set; } = Buttons.A;
    public Buttons Attack { get; set; } = Buttons.X;
    public Buttons Tongue { get; set; } = Buttons.B;
    public Buttons Strafe { get; set; } = Buttons.LeftShoulder;

    internal Buttons this[int action]
    {
        get =>
            action switch
            {
                0 => Left,
                1 => Right,
                2 => Up,
                3 => Down,
                4 => Jump,
                5 => Attack,
                6 => Tongue,
                7 => Strafe,
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
        set
        {
            switch (action)
            {
                case 0:
                    Left = value;
                    break;
                case 1:
                    Right = value;
                    break;
                case 2:
                    Up = value;
                    break;
                case 3:
                    Down = value;
                    break;
                case 4:
                    Jump = value;
                    break;
                case 5:
                    Attack = value;
                    break;
                case 6:
                    Tongue = value;
                    break;
                case 7:
                    Strafe = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    internal void Normalize()
    {
        var defaults = new PadBindings();
        var allowed = BindableButtons.Aggregate(Buttons.None, (mask, button) => mask | button);
        for (int action = 0; action < 8; action++)
            if (this[action] == Buttons.None || (this[action] & ~allowed) != 0)
                this[action] = defaults[action];
    }

    internal static bool IsDown(GamePadState pad, Buttons binding)
    {
        foreach (var button in BindableButtons)
            if ((binding & button) != 0 && ButtonDown(pad, button))
                return true;
        return false;
    }

    private static bool ButtonDown(GamePadState pad, Buttons button) =>
        button switch
        {
            Buttons.LeftTrigger => pad.Triggers.Left > .3f,
            Buttons.RightTrigger => pad.Triggers.Right > .3f,
            Buttons.LeftThumbstickLeft => pad.ThumbSticks.Left.X < -.3f,
            Buttons.LeftThumbstickRight => pad.ThumbSticks.Left.X > .3f,
            Buttons.LeftThumbstickUp => pad.ThumbSticks.Left.Y > .3f,
            Buttons.LeftThumbstickDown => pad.ThumbSticks.Left.Y < -.3f,
            Buttons.RightThumbstickLeft => pad.ThumbSticks.Right.X < -.3f,
            Buttons.RightThumbstickRight => pad.ThumbSticks.Right.X > .3f,
            Buttons.RightThumbstickUp => pad.ThumbSticks.Right.Y > .3f,
            Buttons.RightThumbstickDown => pad.ThumbSticks.Right.Y < -.3f,
            _ => pad.IsButtonDown(button),
        };
}
