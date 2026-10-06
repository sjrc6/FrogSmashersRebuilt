using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed class KeyBindings
{
    public Keys Left { get; set; } = Keys.A;
    public Keys Right { get; set; } = Keys.D;
    public Keys Up { get; set; } = Keys.W;
    public Keys Down { get; set; } = Keys.S;
    public Keys Jump { get; set; } = Keys.T;
    public Keys Attack { get; set; } = Keys.U;
    public Keys Tongue { get; set; } = Keys.Y;
    public Keys Strafe { get; set; } = Keys.R;

    internal Keys this[int action]
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

    internal void RemoveMenuKey(KeyBindings defaults)
    {
        if (Left == Keys.Escape)
            Left = defaults.Left;
        if (Right == Keys.Escape)
            Right = defaults.Right;
        if (Up == Keys.Escape)
            Up = defaults.Up;
        if (Down == Keys.Escape)
            Down = defaults.Down;
        if (Jump == Keys.Escape)
            Jump = defaults.Jump;
        if (Attack == Keys.Escape)
            Attack = defaults.Attack;
        if (Tongue == Keys.Escape)
            Tongue = defaults.Tongue;
        if (Strafe == Keys.Escape)
            Strafe = defaults.Strafe;
    }
}
