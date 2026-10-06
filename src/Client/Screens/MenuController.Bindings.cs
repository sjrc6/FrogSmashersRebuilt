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
            return new("binding-" + action, name, Value: "...");
        if (BindingDevice < 2)
        {
            var key = game.Settings.Keyboard[BindingDevice][action];
            return key == Keys.None
                ? new("binding-" + action, name, BeginBinding, Value: "UNBOUND")
                : new("binding-" + action, name, BeginBinding, Key: key);
        }
        return new(
            "binding-" + action,
            name,
            BeginBinding,
            Button: game.Controls.ControllerBindings(BindingDevice - 2)[action]
        );
    }

    private void BeginBinding()
    {
        WaitingForBinding = true;
    }

    private void ResetBindings()
    {
        if (BindingDevice < 2)
        {
            var defaults = new ClientSettings().Keyboard[BindingDevice];
            bool displaced = false;
            for (int action = 0; action < 8; action++)
                displaced |= game.Settings.BindKey(BindingDevice, action, defaults[action]);
            if (displaced)
                ShowDisplacedBinding();
        }
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
        if (game.Settings.BindKey(BindingDevice, Selected - 1, key))
            ShowDisplacedBinding();
        FinishBinding();
    }

    private void ShowDisplacedBinding() => game.Toasts.Show($"KEY REMOVED FROM KEYBOARD {2 - BindingDevice}");

    private void FinishBinding()
    {
        WaitingForBinding = false;
        game.Controls.ClearPendingEdges();
        menuSoundPending = true;
    }
}
