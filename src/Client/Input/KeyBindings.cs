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
}
