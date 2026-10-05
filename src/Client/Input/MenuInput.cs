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
    bool ApplyAll = false,
    int HorizontalHeld = 0,
    int VerticalHeld = 0,
    bool CpuColor = false,
    bool CpuTeam = false
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
        bool leftHeld =
            (source == null || source < 2)
            && (controls.KeysNow.IsKeyDown(Keys.A) || controls.KeysNow.IsKeyDown(Keys.Left));
        bool rightHeld =
            (source == null || source < 2)
            && (controls.KeysNow.IsKeyDown(Keys.D) || controls.KeysNow.IsKeyDown(Keys.Right));
        bool upHeld =
            (source == null || source < 2)
            && (controls.KeysNow.IsKeyDown(Keys.W) || controls.KeysNow.IsKeyDown(Keys.Up));
        bool downHeld =
            (source == null || source < 2)
            && (controls.KeysNow.IsKeyDown(Keys.S) || controls.KeysNow.IsKeyDown(Keys.Down));
        foreach (int device in source.HasValue ? [source.Value] : new[] { 0 }.Concat(Enumerable.Range(2, 8)))
        {
            vertical += controls.Vertical(device, menu: true);
            horizontal += controls.Horizontal(device, menu: true);
            if (device >= 2)
            {
                var pad = controls.Pads[device - 2];
                upHeld |= pad.IsButtonDown(Buttons.DPadUp) || pad.ThumbSticks.Left.Y > .5f;
                downHeld |= pad.IsButtonDown(Buttons.DPadDown) || pad.ThumbSticks.Left.Y < -.5f;
                leftHeld |= pad.IsButtonDown(Buttons.DPadLeft) || pad.ThumbSticks.Left.X < -.5f;
                rightHeld |= pad.IsButtonDown(Buttons.DPadRight) || pad.ThumbSticks.Left.X > .5f;
            }
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
            Key(Keys.Tab) || Pad(Buttons.Y),
            (rightHeld ? 1 : 0) - (leftHeld ? 1 : 0),
            (downHeld ? 1 : 0) - (upHeld ? 1 : 0),
            Key(Keys.C) || Pad(Buttons.LeftShoulder),
            Key(Keys.T) || Pad(Buttons.RightShoulder)
        );
    }
}
