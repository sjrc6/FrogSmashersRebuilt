using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    public string BindingTitle => game.Controls.DeviceName(BindingDevice);
    public bool ShowStickInputs => Screen == GameScreen.Bindings && BindingDevice >= 2;

    private void OpenBindings(int device)
    {
        BindingDevice = device;
        WaitingForBinding = false;
        RefreshBindingDevice();
        Open(GameScreen.Bindings);
    }

    private void RefreshBindingDevice()
    {
        if (game.Controls.BindingDevices().Contains(BindingDevice))
            return;
        BindingDevice = 0;
        WaitingForBinding = false;
    }

    private void CycleBindingDevice(int direction)
    {
        var devices = game.Controls.BindingDevices();
        int index = Array.IndexOf(devices, BindingDevice);
        BindingDevice = devices[Wrap(index + direction, devices.Length)];
        menuSoundPending = true;
    }

    private MenuEntry BindingEntry(string name, int action)
    {
        if (WaitingForBinding && Selected == action + 1)
            return new(name, Value: "...");
        return BindingDevice < 2
            ? new(name, BeginBinding, Key: BindingKeys()[action])
            : new(name, BeginBinding, Button: game.Controls.ControllerBindings(BindingDevice - 2)[action]);
    }

    private void BeginBinding()
    {
        WaitingForBinding = true;
        game.Toasts.Show(BindingDevice < 2 ? "PRESS A KEY" : "PRESS A BUTTON");
    }

    private Keys[] BindingKeys()
    {
        var keys = game.Settings.Keyboard[BindingDevice];
        return [keys.Left, keys.Right, keys.Up, keys.Down, keys.Jump, keys.Attack, keys.Tongue, keys.Strafe];
    }

    private void ResetBindings()
    {
        if (BindingDevice < 2)
            game.Settings.Keyboard[BindingDevice] = new ClientSettings().Keyboard[BindingDevice];
        else
            game.Controls.ResetControllerBindings(BindingDevice - 2);
        game.Controls.ClearPendingEdges();
    }

    private void CaptureBinding()
    {
        bool cancel = game.Controls.MenuDevice() != null;
        if (cancel)
        {
            FinishBinding();
            return;
        }
        if (BindingDevice >= 2)
        {
            foreach (var button in PadBindings.BindableButtons)
                if (game.Controls.BindingPress(BindingDevice - 2, button))
                {
                    game.Controls.ControllerBindings(BindingDevice - 2)[Selected - 1] = button;
                    FinishBinding();
                    return;
                }
            return;
        }
        var pressed = game.Controls.KeysNow.GetPressedKeys().Where(game.Controls.Press).ToArray();
        if (pressed.Length == 0)
            return;
        var key = pressed[0];
        if (key == Keys.Escape)
        {
            FinishBinding();
            return;
        }
        var keys = game.Settings.Keyboard[BindingDevice];
        switch (Selected - 1)
        {
            case 0:
                keys.Left = key;
                break;
            case 1:
                keys.Right = key;
                break;
            case 2:
                keys.Up = key;
                break;
            case 3:
                keys.Down = key;
                break;
            case 4:
                keys.Jump = key;
                break;
            case 5:
                keys.Attack = key;
                break;
            case 6:
                keys.Tongue = key;
                break;
            case 7:
                keys.Strafe = key;
                break;
        }
        FinishBinding();
    }

    private void FinishBinding()
    {
        WaitingForBinding = false;
        game.Controls.ClearPendingEdges();
        menuSoundPending = true;
    }
}
