using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

internal static class MenuRepeatTests
{
    public static void Run(Action<bool, string> check)
    {
        var repeat = new MenuRepeat(.3, .05);
        check(repeat.Read(1, 1, 0) == 1, "Adjustment responds on the press");
        check(repeat.Read(1, 0, .2) == 0, "Hold waits for the configured delay");
        check(repeat.Read(1, 0, .11) == 1, "Holding repeats after the delay");
        check(repeat.Read(-1, -1, 0) == -1, "Reversing direction responds immediately");
        check(repeat.Read(-1, 0, .2) == 0, "Reversing direction resets the delay");
        check(repeat.Read(0, 0, 1) == 0, "Release stops repeat");
        repeat.Reset();
        check(repeat.Read(1, 0, 1) == 0, "Changing menu target cannot carry a pending repeat");
        var counts = new List<int>();
        foreach (int fps in new[] { 30, 60, 144, 240 })
        {
            repeat.Reset();
            int steps = repeat.Read(1, 1, 0);
            for (int frame = 0; frame < fps * 2; frame++)
                steps += repeat.Read(1, 0, 1d / fps);
            counts.Add(steps);
        }
        check(counts.Min() > 1 && counts.Max() - counts.Min() <= 1, "Repeat cadence is independent of render rate");
        var keys = new KeyboardState(Keys.Left);
        var pad = default(GamePadState);
        var controls = new Controls(new())
        {
            KeyboardSource = () => keys,
            GamePadSource = index => index == 0 ? pad : default,
            MouseSource = () => default,
        };
        controls.Poll();
        controls.Poll();
        check(
            MenuInput.Read(controls).HorizontalHeld == -1 && MenuInput.Read(controls).Horizontal == 0,
            "Held arrows are available separately from press edges"
        );
        keys = new(Keys.Up);
        controls.Poll();
        controls.Poll();
        check(
            MenuInput.Read(controls).VerticalHeld == -1 && MenuInput.Read(controls).Vertical == 0,
            "Vertical navigation separates held direction from fresh presses"
        );
        keys = new(Keys.A, Keys.D);
        controls.Poll();
        check(MenuInput.Read(controls).HorizontalHeld == 0, "Opposing adjustments cancel");
        keys = default;
        pad = new(Vector2.UnitX, Vector2.Zero, 0, 0, Array.Empty<Buttons>());
        controls.Poll();
        controls.Poll();
        check(MenuInput.Read(controls).HorizontalHeld == 1, "Held controller stick adjusts numeric values");
        pad = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.DPadLeft);
        controls.Poll();
        check(MenuInput.Read(controls).HorizontalHeld == -1, "Held controller D-pad adjusts numeric values");
    }
}
