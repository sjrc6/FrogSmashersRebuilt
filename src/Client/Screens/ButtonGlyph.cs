using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal readonly record struct ButtonGlyph(string Path)
{
    public static ButtonGlyph Pad(string name) => new("UI/Buttons/xbox/" + name);

    public static ButtonGlyph Pad(Buttons button) =>
        Pad(
            button switch
            {
                Buttons.A => "oButton",
                Buttons.B => "aButton",
                Buttons.X => "uButton",
                Buttons.Y => "yButton",
                Buttons.LeftShoulder => "leftBumper",
                Buttons.RightShoulder => "rightBumper",
                Buttons.LeftTrigger => "leftTrigger",
                Buttons.RightTrigger => "rightTrigger",
                Buttons.Back => "selectButton",
                Buttons.Start => "startButton",
                Buttons.DPadLeft => "dPadLeft",
                Buttons.DPadRight => "dPadRight",
                Buttons.DPadUp => "dPadUp",
                Buttons.DPadDown => "dPadDown",
                Buttons.RightStick
                or Buttons.RightThumbstickLeft
                or Buttons.RightThumbstickRight
                or Buttons.RightThumbstickUp
                or Buttons.RightThumbstickDown => "rightStick",
                _ => "leftStick",
            }
        );

    public static ButtonGlyph[] PadBinding(Buttons binding) =>
        PadBindings.BindableButtons.Where(button => (binding & button) != 0).Select(Pad).Distinct().ToArray();

    public static ButtonGlyph Accept(int device) => device >= 2 ? Pad("oButton") : Key(Keys.Enter);

    public static ButtonGlyph Back(int device) => device >= 2 ? Pad("aButton") : Key(Keys.Escape);

    public static ButtonGlyph Remove(int device) => device >= 2 ? Pad(Buttons.X) : Key(Keys.U);

    public static ButtonGlyph ApplyAll(int device) => device >= 2 ? Pad(Buttons.Y) : Key(Keys.Tab);

    public static ButtonGlyph Menu(int device) => device >= 2 ? Pad("startButton") : Key(Keys.Escape);

    public static ButtonGlyph Key(Keys key) =>
        HasKeyIcon(key) ? new($"UI/Buttons/keyboard/baked/{key}") : new("UI/Buttons/keyboard/key");

    private static bool HasKeyIcon(Keys key) =>
        key
            is >= Keys.A
                and <= Keys.Z
                or >= Keys.D0
                and <= Keys.D9
                or >= Keys.NumPad0
                and <= Keys.Divide
                or >= Keys.F1
                and <= Keys.F12
                or Keys.Back
                or Keys.Tab
                or Keys.Enter
                or Keys.Pause
                or Keys.CapsLock
                or Keys.Escape
                or Keys.Space
                or Keys.PageUp
                or Keys.PageDown
                or Keys.End
                or Keys.Home
                or Keys.Left
                or Keys.Up
                or Keys.Right
                or Keys.Down
                or Keys.PrintScreen
                or Keys.Insert
                or Keys.Delete
                or Keys.LeftWindows
                or Keys.RightWindows
                or Keys.Apps
                or Keys.NumLock
                or Keys.Scroll
                or Keys.LeftShift
                or Keys.RightShift
                or Keys.LeftControl
                or Keys.RightControl
                or Keys.LeftAlt
                or Keys.RightAlt
                or Keys.OemSemicolon
                or Keys.OemPlus
                or Keys.OemComma
                or Keys.OemMinus
                or Keys.OemPeriod
                or Keys.OemQuestion
                or Keys.OemTilde
                or Keys.OemOpenBrackets
                or Keys.OemPipe
                or Keys.OemCloseBrackets
                or Keys.OemQuotes
                or Keys.OemBackslash;
}
