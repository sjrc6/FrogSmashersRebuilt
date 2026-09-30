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
