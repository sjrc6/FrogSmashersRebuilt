using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

internal static class InputTests
{
    public static void Run(Action<bool, string> check)
    {
        var settings = new ClientSettings();
        var controls = new FrogSmashers.Client.Controls(settings);
        KeyboardState keyboard = default;
        controls.KeyboardSource = () => keyboard;
        check(
            settings.Keyboard[0].Strafe == Keys.R && settings.Keyboard[1].Strafe == Keys.N,
            "keyboard strafe defaults differ from the fork"
        );
        keyboard = new(Keys.A, Keys.R);
        controls.Poll();
        var strafe = controls.Read(0);
        check(strafe.X == -1 && strafe.Strafe, "WASD strafe input missing");
        keyboard = default;
        controls.Poll();
        check(!controls.Read(0).Strafe, "strafe release buffered incorrectly");
        keyboard = new(Keys.T);
        controls.Poll();
        keyboard = default;
        controls.Poll();
        check(controls.Read(0).Jump && !controls.Read(0).Jump, "jump edge must survive exactly one consumed tick");
        keyboard = new(Keys.R);
        controls.Poll();
        keyboard = default;
        controls.Poll();
        check(!controls.Read(0).Strafe, "held strafe must not become a buffered action");
        keyboard = new(Keys.Left, Keys.N);
        controls.Poll();
        var arrows = controls.Read(1);
        check(arrows.X == -1 && arrows.Strafe, "arrow layout strafe input missing");
        controls.Pads[0] = new GamePadState(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.LeftShoulder);
        check(controls.Read(2).Strafe, "controller shoulder strafe input missing");
        Console.WriteLine("Keyboard/controller strafe and render-frame input buffering checks passed");
    }
}
