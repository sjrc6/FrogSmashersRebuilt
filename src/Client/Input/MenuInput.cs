using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal readonly record struct MenuInput(bool Accept, bool Back, int Vertical, int Horizontal)
{
    public static MenuInput Read(Controls controls, int? owner = null)
    {
        bool Key(Keys key) => (owner == null || owner < 2) && controls.Press(key);
        bool Pad(Buttons button) =>
            owner == null ? controls.AnyPad(button) : owner >= 2 && controls.PadPress(owner.Value - 2, button);
        bool accept = Key(Keys.Enter) || Pad(Buttons.A);
        bool back = Key(Keys.Escape) || Pad(Buttons.B) || Pad(Buttons.Back) || Pad(Buttons.Start);
        int vertical = 0,
            horizontal = 0;
        foreach (int device in owner.HasValue ? [owner.Value] : Enumerable.Range(0, 10))
        {
            vertical = Math.Clamp(vertical + controls.Vertical(device, menu: true), -1, 1);
            horizontal = Math.Clamp(horizontal + controls.Horizontal(device, menu: true), -1, 1);
        }
        return new MenuInput(accept, back, vertical, horizontal);
    }
}
