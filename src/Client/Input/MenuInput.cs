using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal readonly record struct MenuInput(bool Accept, bool Back, int Vertical, int Horizontal)
{
    public static MenuInput Read(Controls controls)
    {
        bool accept = controls.Press(Keys.Enter) || controls.AnyPad(Buttons.A);
        bool back = controls.Press(Keys.Escape) || controls.AnyPad(Buttons.Back);
        int vertical =
            (controls.Press(Keys.Down) || controls.AnyPad(Buttons.DPadDown) ? 1 : 0)
            - (controls.Press(Keys.Up) || controls.AnyPad(Buttons.DPadUp) ? 1 : 0);
        int horizontal =
            (controls.Press(Keys.Right) || controls.AnyPad(Buttons.DPadRight) ? 1 : 0)
            - (controls.Press(Keys.Left) || controls.AnyPad(Buttons.DPadLeft) ? 1 : 0);
        return new MenuInput(accept, back, vertical, horizontal);
    }
}
