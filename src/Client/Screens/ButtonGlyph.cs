using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal readonly record struct ButtonGlyph(string Path)
{
    public static ButtonGlyph Pad(string name) => new("UI/Buttons/xbox/" + name);

    public static ButtonGlyph Accept(int? owner) => owner >= 2 ? Pad("oButton") : Key(Keys.Enter);

    public static ButtonGlyph Back(int? owner) => owner >= 2 ? Pad("aButton") : Key(Keys.Escape);

    public static ButtonGlyph Start(int device) =>
        device >= 2 ? Pad("startButton") : Key(device == 0 ? Keys.Space : Keys.RightShift);

    public static ButtonGlyph Menu(int? owner) => owner >= 2 ? Pad("startButton") : Key(Keys.Escape);

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
