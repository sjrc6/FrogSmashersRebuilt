using System.Text.Json;
using FrogSmashers.Client;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

internal static class ControllerBindingTests
{
    public static void Run(Action<bool, string> check)
    {
        var reserved = new PadBindings { Jump = Buttons.Back, Attack = Buttons.Start };
        reserved.Normalize();
        check(
            reserved.Jump == Buttons.A && reserved.Attack == Buttons.X,
            "Select and Start are reserved and cannot remain bound to gameplay"
        );
        var directionSettings = new ClientSettings();
        var keyboard = new KeyboardState();
        var directions = new Controls(directionSettings)
        {
            KeyboardSource = () => keyboard,
            MouseSource = () => default,
            GamePadSource = _ => default,
        };
        directions.Poll();
        directionSettings.Keyboard[0].Left = Keys.J;
        directionSettings.Keyboard[0].Right = Keys.L;
        keyboard = new KeyboardState(Keys.L);
        directions.Poll();
        check(
            directions.HorizontalPress(0) == 1 && directions.HorizontalPress(1) == 0,
            "Lobby direction changes follow remaps and device ownership"
        );
        directions.Poll();
        check(directions.HorizontalPress(0) == 0, "Holding a direction does not cycle every render frame");
        keyboard = new KeyboardState(Keys.J);
        directions.Poll();
        check(directions.HorizontalPress(0) == -1, "Remapped left selects backward");
        var settings = new ClientSettings();
        var pads = new GamePadState[8];
        var devices = new ControllerDevice[8];
        devices[3] = new("Test Controller", "guid-a");
        var controls = new Controls(settings)
        {
            KeyboardSource = () => default,
            MouseSource = () => default,
            GamePadSource = index => pads[index],
            ControllerSource = index => devices[index],
        };
        controls.Poll();
        check(controls.BindingDevices().SequenceEqual([0, 1]), "two keyboard pages exist without controllers");
        check(settings.ControllerBindings.Count == 0, "disconnected ports do not create saved mappings");
        pads[3] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.None);
        controls.Poll();
        check(
            controls.BindingDevices().SequenceEqual([0, 1, 5]),
            "device list includes nonconsecutive connected ports"
        );
        pads[3] = default;
        controls.Poll();
        check(controls.BindingDevices().SequenceEqual([0, 1]), "disconnected controller leaves the page list");

        devices[0] = devices[3];
        devices[1] = new("Test Controller", "guid-b");
        pads[0] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.None);
        controls.Poll();
        var bindings = controls.ControllerBindings(0);
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
            $"custom controller actions drive gameplay: {input}"
        );
        check(MenuInput.Read(controls, 2).Back, "B still backs out of menus after gameplay rebinding");
        pads[0] = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A | Buttons.X | Buttons.LeftShoulder);
        controls.Poll();
        input = controls.Read(2);
        check(!input.Jump && !input.Attack && !input.Strafe, "replaced buttons no longer drive old actions");
        check(MenuInput.Read(controls, 2).Accept, "A still confirms menus after gameplay rebinding");
        pads[1] = pads[0];
        controls.Poll();
        input = controls.Read(3);
        check(
            input.Jump && input.Attack && input.Strafe,
            "different controller identifiers retain independent bindings even with identical names"
        );

        pads[0] = default;
        controls.Poll();
        devices[5] = new("Renamed Controller", "GUID-A");
        pads[5] = new(Vector2.Zero, Vector2.Zero, 0, 1, Buttons.B | Buttons.Y | Buttons.RightShoulder);
        controls.Poll();
        input = controls.Read(7);
        check(
            input.Jump && input.Attack && input.Tongue && input.Strafe,
            "controller bindings follow the identifier to another port and display name"
        );
        devices[0] = devices[1];
        pads[0] = pads[1];
        controls.Poll();
        check(controls.Read(2).Jump, "another controller does not inherit the previous port occupant's mapping");
        controls.ControllerBindings(0).Jump = Buttons.Y;
        check(controls.ControllerBindings(1).Jump == Buttons.Y, "Controllers sharing an identity share edits");
        controls.ControllerBindings(0).Jump = Buttons.A;
        check(settings.ControllerBindings.Count == 2, "port changes do not create extra saved mappings");

        var loaded = ClientSettings.Parse(JsonSerializer.Serialize(settings));
        check(
            loaded.ControllerBindings["id:guid-a"].Jump == Buttons.B
                && loaded.ControllerBindings["id:guid-b"].Jump == Buttons.A,
            "controller mappings persist by identity"
        );
        controls.ControllerBindings(5).Jump = Buttons.Start;
        controls.ControllerBindings(5).Attack = Buttons.None;
        loaded = ClientSettings.Parse(JsonSerializer.Serialize(settings));
        check(
            loaded.ControllerBindings["id:guid-a"].Jump == Buttons.A
                && loaded.ControllerBindings["id:guid-a"].Attack == Buttons.X,
            "invalid and reserved controller bindings normalize to defaults"
        );
        controls.ControllerBindings(1).Jump = Buttons.Y;
        controls.ResetControllerBindings(0);
        check(
            controls.ControllerBindings(1).Jump == Buttons.A,
            "reset applies to all connected controllers with the same identity"
        );
        check(
            new ControllerDevice("Named Pad", null).BindingKey == new ControllerDevice("named pad", "").BindingKey,
            "missing identifiers fall back to normalized device names"
        );
        check(
            new ControllerDevice(null, null).BindingKey == "generic",
            "unnamed unidentified controllers use generic defaults"
        );
        Console.WriteLine(
            "Controller identity, reconnects, shared mappings, fixed menu controls and persistence passed"
        );
    }
}
