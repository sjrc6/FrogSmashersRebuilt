using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

internal static class MenuRepeatTests
{
    public static void Run(Action<bool, string> check)
    {
        var repeat = new MenuRepeat();
        check(repeat.Read(1, 1, 0) == 1, "Adjustment responds on the press");
        check(repeat.Read(1, 0, .34) == 0, "Hold has an initial grace period");
        check(repeat.Read(1, 0, .02) == 1, "Holding repeats after the grace period");
        check(repeat.Read(1, 0, .04) == 0, "Hold does not repeat every render frame");
        check(repeat.Read(1, 0, .04) == 1, "Repeat cadence follows elapsed time");
        check(repeat.Read(-1, -1, .01) == -1, "Reversing direction responds immediately");
        check(repeat.Read(-1, 0, .1) == 0, "Reversing direction resets the grace period");
        check(repeat.Read(0, 0, 1) == 0, "Release stops repeat");
        check(repeat.Read(1, 1, 0) == 1, "A new press responds immediately");
        repeat.Reset();
        check(repeat.Read(1, 0, 1) == 0, "Changing menu target cannot carry a pending repeat");
        check(repeat.Read(1, 0, 1) == 0, "Opening a menu with confirm held cannot start changing its first setting");
        foreach (int fps in new[] { 30, 60, 144, 240 })
        {
            repeat.Reset();
            int steps = repeat.Read(1, 1, 0);
            for (int frame = 0; frame < fps * 2; frame++)
                steps += repeat.Read(1, 0, 1d / fps);
            check(steps is >= 22 and <= 24, $"Numeric repeat is independent of {fps} Hz rendering");
        }

        foreach (int fps in new[] { 30, 60, 144, 240 })
        {
            var navigation = new MenuRepeat(.3, .05);
            int steps = navigation.Read(1, 1, 0);
            for (int frame = 0; frame < fps * 2; frame++)
                steps += navigation.Read(1, 0, 1d / fps);
            check(steps is >= 34 and <= 36, $"Fast navigation repeats consistently at {fps} Hz");
        }
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
        check(ConnectionOverlay.PingColor(149) == new Color(163, 206, 39), "Low RTT uses DGR green");
        check(ConnectionOverlay.PingColor(150) == new Color(247, 224, 90), "150 ms uses DGR yellow");
        check(ConnectionOverlay.PingColor(250) == new Color(192, 32, 45), "250 ms uses DGR red");
        check(ConnectionOverlay.PingColor(null) == Color.Gray, "Unknown RTT is not shown as a good connection");
    }
}
