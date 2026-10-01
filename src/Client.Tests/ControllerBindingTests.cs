using System.Text.Json;
using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

internal static class ControllerBindingTests
{
    public static void Run(Action<bool, string> check)
    {
        var settings = new ClientSettings();
        var pads = new GamePadState[8];
        var controls = new Controls(settings)
        {
            KeyboardSource = () => default,
            MouseSource = () => default,
            GamePadSource = index => pads[index],
            GamePadNameSource = index => index == 3 ? "Test Controller" : "",
        };
        controls.Poll();
        check(controls.BindingDevices().SequenceEqual([0, 1]), "two keyboard pages exist without controllers");
        pads[3] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.None);
        controls.Poll();
        check(
            controls.BindingDevices().SequenceEqual([0, 1, 5]),
            "device list includes nonconsecutive connected ports"
        );
        check(controls.DeviceName(5) == "TEST CONTROLLER", "controller page uses reported product name");
        check(
            controls.DeviceName(0) == "KEYBOARD 1" && controls.DeviceName(2) == "CONTROLLER 1",
            "device names have readable fallbacks"
        );
        pads[3] = default;
        controls.Poll();
        check(controls.BindingDevices().SequenceEqual([0, 1]), "disconnected controller leaves the page list");

        var bindings = settings.Controllers[0];
        bindings.Jump = Buttons.B;
        bindings.Attack = Buttons.RightTrigger;
        bindings.Tongue = Buttons.Y;
        bindings.Strafe = Buttons.RightShoulder;
        bindings.Left = Buttons.RightThumbstickLeft;
        pads[0] = new(Vector2.Zero, new Vector2(-1, 0), 0, 1, Buttons.B | Buttons.Y | Buttons.RightShoulder);
        controls.Poll();
        var input = controls.Read(2);
        check(
            input.Jump && input.Attack && input.Tongue && input.Strafe && input.X == -1,
            $"custom pad actions, triggers and stick direction drive gameplay: {input}"
        );
        check(MenuInput.Read(controls, 2).Back, "B still backs out of menus after gameplay rebinding");
        pads[0] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A | Buttons.X | Buttons.LeftShoulder);
        controls.Poll();
        input = controls.Read(2);
        check(!input.Jump && !input.Attack && !input.Strafe, "replaced controller buttons no longer drive old actions");
        check(MenuInput.Read(controls, 2).Accept, "A still confirms menus after gameplay rebinding");
        pads[1] = pads[0];
        controls.Poll();
        input = controls.Read(3);
        check(input.Jump && input.Attack && input.Strafe, "controller binding edits remain independent per port");

        var loaded = ClientSettings.Parse(JsonSerializer.Serialize(settings));
        check(
            loaded.Controllers[0].Jump == Buttons.B
                && loaded.Controllers[0].Attack == Buttons.RightTrigger
                && loaded.Controllers[1].Jump == Buttons.A,
            "controller bindings survive save/load independently"
        );
        settings.Controllers[0].Jump = Buttons.Start;
        settings.Controllers[0].Attack = Buttons.None;
        loaded = ClientSettings.Parse(JsonSerializer.Serialize(settings));
        check(
            loaded.Controllers[0].Jump == Buttons.A && loaded.Controllers[0].Attack == Buttons.X,
            "invalid and reserved controller bindings normalize to defaults"
        );
        Console.WriteLine("Controller device discovery, bindings, fixed menu controls and persistence passed");
    }
}
