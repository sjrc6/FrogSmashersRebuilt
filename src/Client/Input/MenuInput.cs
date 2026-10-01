using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal readonly record struct MenuInput(
    bool Accept,
    bool Back,
    int Vertical,
    int Horizontal,
    bool AcceptHeld = false,
    bool AcceptReleased = false,
    bool Remove = false,
    bool ApplyAll = false
)
{
    public static MenuInput Read(Controls controls, int? source = null)
    {
        bool Key(Keys key) => (source == null || source < 2) && controls.Press(key);
        bool Pad(Buttons button) =>
            source == null ? controls.AnyPad(button) : source >= 2 && controls.PadPress(source.Value - 2, button);
        bool accept = Key(Keys.Enter) || Pad(Buttons.A);
        bool back = Key(Keys.Escape) || Pad(Buttons.B) || Pad(Buttons.Back) || Pad(Buttons.Start);
        int vertical = 0,
            horizontal = 0;
        foreach (int device in source.HasValue ? [source.Value] : new[] { 0 }.Concat(Enumerable.Range(2, 8)))
        {
            vertical += controls.Vertical(device, menu: true);
            horizontal += controls.Horizontal(device, menu: true);
        }
        bool held = (source == null || source < 2) && controls.KeysNow.IsKeyDown(Keys.Enter);
        bool released = (source == null || source < 2) && controls.Release(Keys.Enter);
        foreach (int device in source.HasValue ? [source.Value] : Enumerable.Range(2, 8))
            if (device >= 2)
            {
                held |= controls.Pads[device - 2].IsButtonDown(Buttons.A);
                released |= controls.PadRelease(device - 2, Buttons.A);
            }
        return new MenuInput(
            accept,
            back,
            Math.Sign(vertical),
            Math.Sign(horizontal),
            held,
            released && !held,
            Key(Keys.U) || Pad(Buttons.X),
            Key(Keys.Tab) || Pad(Buttons.Y)
        );
    }
}
