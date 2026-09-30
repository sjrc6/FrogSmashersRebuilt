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
        keyboard = new(Keys.U, Keys.M);
        controls.Poll();
        keyboard = default;
        controls.Poll();
        controls.ClearPendingEdges(0);
        check(
            !controls.Read(0).Attack && controls.Read(1).Jump,
            "spawning clears only that device's pending lobby actions"
        );
        HorizontalPriority(check);
        MouseHover(check);
        Console.WriteLine("Keyboard/controller strafe and render-frame input buffering checks passed");
    }

    private static void MouseHover(Action<bool, string> check)
    {
        MouseState mouse = default;
        KeyboardState keyboard = default;
        var controls = new Controls(new())
        {
            KeyboardSource = () => keyboard,
            GamePadSource = _ => default,
            MouseSource = () => mouse,
        };
        void Move(int x, int y, ButtonState button = ButtonState.Released)
        {
            mouse = new(
                x,
                y,
                0,
                button,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released,
                ButtonState.Released
            );
            controls.Poll();
        }

        Move(640, 174);
        check(controls.MouseMoved, "mouse motion activates menu hover");
        controls.SuspendMouseHover();
        check(!controls.MouseMoved, "display changes suppress hover in the current update");
        Move(960, 261);
        check(!controls.MouseMoved, "resizing cannot move menu selection with remapped mouse coordinates");
        Move(962, 263);
        check(!controls.MouseMoved, "hover stays suspended while resize events settle");
        keyboard = new(Keys.Down);
        Move(963, 264, ButtonState.Pressed);
        check(
            !controls.MouseMoved && controls.MousePressed && controls.Press(Keys.Down),
            "resize suppression leaves clicks and keyboard navigation available"
        );
        controls.Poll();
        check(!controls.MouseMoved && !controls.MousePressed, "stationary pointer resets the hover baseline");
        Move(964, 265);
        check(controls.MouseMoved && !controls.MousePressed, "normal hover resumes after the pointer settles");
    }

    private static void HorizontalPriority(Action<bool, string> check)
    {
        var settings = new ClientSettings();
        settings.Keyboard[1].Left = Keys.J;
        settings.Keyboard[1].Right = Keys.L;
        KeyboardState keyboard = default;
        GamePadState pad = default;
        var controls = new Controls(settings)
        {
            KeyboardSource = () => keyboard,
            GamePadSource = index => index == 0 ? pad : default,
            MouseSource = () => default,
        };

        foreach (int device in new[] { 0, 1, 2 })
        {
            void Hold(bool left, bool right)
            {
                var bindings = settings.Keyboard[Math.Min(device, 1)];
                keyboard =
                    device < 2
                        ? new KeyboardState([
                            .. left ? new[] { bindings.Left } : [],
                            .. right ? new[] { bindings.Right } : [],
                        ])
                        : default;
                pad =
                    device == 2
                        ? new(
                            Vector2.Zero,
                            Vector2.Zero,
                            0,
                            0,
                            (left ? Buttons.DPadLeft : 0) | (right ? Buttons.DPadRight : 0)
                        )
                        : default;
                controls.Poll();
            }

            foreach (bool leftFirst in new[] { true, false })
            {
                sbyte first = (sbyte)(leftFirst ? -1 : 1);
                Hold(false, false);
                Hold(leftFirst, !leftFirst);
                check(controls.Read(device).X == first, $"device {device}: one held direction");
                Hold(true, true);
                check(controls.Read(device, false).X == -first, $"device {device}: latest direction wins");
                check(controls.Read(device).X == -first, $"device {device}: peeking does not change priority");
                controls.Poll();
                check(controls.Read(device).X == -first, $"device {device}: priority survives held input");
                Hold(leftFirst, !leftFirst);
                check(controls.Read(device).X == first, $"device {device}: releasing newest falls back to older");
                Hold(true, true);
                check(controls.Read(device).X == -first, $"device {device}: pressing again wins again");
                Hold(!leftFirst, leftFirst);
                check(controls.Read(device).X == -first, $"device {device}: releasing older preserves newest");
            }
            Hold(false, false);
            Hold(true, true);
            check(controls.Read(device).X == 0, $"device {device}: simultaneous presses are neutral");
            controls.Poll();
            check(controls.Read(device).X == 0, $"device {device}: simultaneous hold stays neutral");
            Hold(true, false);
            check(controls.Read(device).X == -1, $"device {device}: release resolves a simultaneous hold");
            Hold(false, false);
            controls.Read(device);
            Hold(true, false);
            check(controls.Read(device, false).X == -1, $"device {device}: first poll can be inspected before a tick");
            Hold(true, true);
            check(controls.Read(device).X == 0, $"device {device}: two render polls in the same tick stay neutral");
            Hold(false, false);
        }

        keyboard = new(Keys.A, Keys.L);
        pad = new(new Vector2(-1, 0), Vector2.Zero, 0, 0, Buttons.None);
        controls.Poll();
        controls.Read(0);
        controls.Read(1);
        controls.Read(2);
        keyboard = new(Keys.A, Keys.D, Keys.J, Keys.L);
        pad = new(new Vector2(-1, 0), Vector2.Zero, 0, 0, Buttons.DPadRight);
        controls.Poll();
        check(
            controls.Read(0).X == 1 && controls.Read(1).X == -1,
            "keyboard layouts keep independent direction history"
        );
        check(controls.Read(2).X == 1, "new d-pad input overrides a held opposite stick");
        pad = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.DPadRight);
        controls.Poll();
        pad = new(new Vector2(-1, 0), Vector2.Zero, 0, 0, Buttons.DPadRight);
        controls.Poll();
        check(controls.Read(2).X == -1, "new stick input overrides a held opposite d-pad");
        pad = default;
        controls.Poll();
        check(controls.Read(2).X == 0, "disconnected controller clears direction history");
        pad = new(new Vector2(-1, 0), Vector2.Zero, 0, 0, Buttons.DPadRight);
        controls.Poll();
        check(controls.Read(2).X == 0, "simultaneous stick and d-pad are neutral after reconnecting");
    }
}
