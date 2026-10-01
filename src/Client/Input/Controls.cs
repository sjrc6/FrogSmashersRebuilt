using FrogSmashers.Core;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed class Controls
{
    private const int KeyboardCount = 2;
    private const int ControllerCount = 8;
    private readonly ClientSettings settings;
    public KeyboardState KeysNow { get; private set; }
    public KeyboardState KeysBefore { get; private set; }
    public GamePadState[] Pads { get; } = new GamePadState[ControllerCount];

    private readonly GamePadState[] beforePads = new GamePadState[ControllerCount];
    private readonly ControllerDevice?[] controllerDevices = new ControllerDevice?[ControllerCount];
    private readonly HorizontalInput[] horizontal = new HorizontalInput[KeyboardCount + ControllerCount];
    private readonly InputButtons[] held = new InputButtons[KeyboardCount + ControllerCount];
    private readonly InputButtons[] edges = new InputButtons[KeyboardCount + ControllerCount];
    public Func<KeyboardState>? KeyboardSource { get; set; }
    public Func<int, GamePadState>? GamePadSource { get; set; }
    internal Func<int, ControllerDevice>? ControllerSource { get; set; }
    public Func<MouseState>? MouseSource { get; set; }
    public MouseState MouseNow { get; private set; }
    private MouseState mouseBefore;
    private bool mouseActive = true;
    private bool mouseHoverSuspended;
    public bool MouseMoved => mouseActive && !mouseHoverSuspended && MouseNow.Position != mouseBefore.Position;
    public bool MousePressed =>
        mouseActive && MouseNow.LeftButton == ButtonState.Pressed && mouseBefore.LeftButton == ButtonState.Released;
    public bool MouseRightPressed =>
        mouseActive && MouseNow.RightButton == ButtonState.Pressed && mouseBefore.RightButton == ButtonState.Released;

    public Controls(ClientSettings settings) => this.settings = settings;

    public void SuspendMouseHover() => mouseHoverSuspended = true;

    public void ClearPendingEdges()
    {
        for (int device = 0; device < edges.Length; device++)
            ClearPendingEdges(device);
    }

    public void ClearPendingEdges(int device)
    {
        edges[device] = 0;
        horizontal[device].AdvanceTick();
    }

    public void Poll(bool windowActive = true)
    {
        KeysBefore = KeysNow;
        KeysNow = KeyboardSource?.Invoke() ?? Keyboard.GetState();
        mouseBefore = MouseNow;
        MouseNow = MouseSource?.Invoke() ?? Mouse.GetState();
        if (!windowActive || !mouseActive)
            mouseBefore = MouseNow;
        mouseActive = windowActive;
        if (MouseNow.Position == mouseBefore.Position)
            mouseHoverSuspended = false;
        for (int i = 0; i < ControllerCount; i++)
        {
            beforePads[i] = Pads[i];
            Pads[i] = GamePadSource?.Invoke(i) ?? GamePad.GetState(i, GamePadDeadZone.None);
            if (!Pads[i].IsConnected || !beforePads[i].IsConnected)
                controllerDevices[i] = null;
        }

        for (int i = 0; i < KeyboardCount + ControllerCount; i++)
        {
            var (left, right) = HorizontalHeld(i);
            horizontal[i].Update(left, right);
            var b = ReadRaw(i).Buttons;
            edges[i] |= b & ~held[i] & ~InputButtons.Strafe;
            held[i] = b;
        }
    }

    public bool Press(Keys key) => KeysNow.IsKeyDown(key) && KeysBefore.IsKeyUp(key);

    public bool PadPress(int i, Buttons button) => Pads[i].IsButtonDown(button) && beforePads[i].IsButtonUp(button);

    public bool PadRelease(int i, Buttons button) => Pads[i].IsButtonUp(button) && beforePads[i].IsButtonDown(button);

    public bool Release(Keys key) => KeysNow.IsKeyUp(key) && KeysBefore.IsKeyDown(key);

    internal bool BindingPress(int pad, Buttons binding) =>
        PadBindings.IsDown(Pads[pad], binding) && !PadBindings.IsDown(beforePads[pad], binding);

    internal int[] BindingDevices() =>
        [0, 1, .. Enumerable.Range(0, ControllerCount).Where(pad => Pads[pad].IsConnected).Select(pad => pad + 2)];

    internal string DeviceName(int device)
    {
        if (device < 2)
            return $"KEYBOARD {device + 1}";
        string? name = Controller(device - 2).Name;
        return string.IsNullOrWhiteSpace(name) ? $"CONTROLLER {device - 1}" : name.ToUpperInvariant();
    }

    private ControllerDevice Controller(int pad)
    {
        if (controllerDevices[pad] is { } device)
            return device;
        if (ControllerSource != null)
            device = ControllerSource(pad);
        else
        {
            var capabilities = GamePad.GetCapabilities(pad);
            device = new(capabilities.DisplayName, capabilities.Identifier);
        }
        controllerDevices[pad] = device;
        return device;
    }

    internal PadBindings ControllerBindings(int pad)
    {
        string key = Controller(pad).BindingKey;
        if (!settings.ControllerBindings.TryGetValue(key, out var bindings))
            settings.ControllerBindings[key] = bindings = new();
        return bindings;
    }

    internal void ResetControllerBindings(int pad)
    {
        settings.ControllerBindings.Remove(Controller(pad).BindingKey);
        ClearPendingEdges();
    }

    public bool AnyPad(Buttons button)
    {
        for (int index = 0; index < ControllerCount; index++)
        {
            if (PadPress(index, button))
                return true;
        }
        return false;
    }

    public int Horizontal(int device, bool menu = false)
    {
        if (device < 2)
        {
            bool right = Press(device == 0 ? Keys.D : Keys.Right) || menu && Press(Keys.Right) || menu && Press(Keys.D);
            bool left = Press(device == 0 ? Keys.A : Keys.Left) || menu && Press(Keys.Left) || menu && Press(Keys.A);
            return (right ? 1 : 0) - (left ? 1 : 0);
        }
        int pad = device - 2;
        bool rightPad =
            PadPress(pad, Buttons.DPadRight)
            || Pads[pad].ThumbSticks.Left.X > .5f && beforePads[pad].ThumbSticks.Left.X <= .5f;
        bool leftPad =
            PadPress(pad, Buttons.DPadLeft)
            || Pads[pad].ThumbSticks.Left.X < -.5f && beforePads[pad].ThumbSticks.Left.X >= -.5f;
        return (rightPad ? 1 : 0) - (leftPad ? 1 : 0);
    }

    public int Vertical(int device, bool menu = false)
    {
        if (device < 2)
        {
            bool down = Press(device == 0 ? Keys.S : Keys.Down) || menu && Press(Keys.Down) || menu && Press(Keys.S);
            bool up = Press(device == 0 ? Keys.W : Keys.Up) || menu && Press(Keys.Up) || menu && Press(Keys.W);
            return (down ? 1 : 0) - (up ? 1 : 0);
        }
        int pad = device - 2;
        bool downPad =
            PadPress(pad, Buttons.DPadDown)
            || Pads[pad].ThumbSticks.Left.Y < -.5f && beforePads[pad].ThumbSticks.Left.Y >= -.5f;
        bool upPad =
            PadPress(pad, Buttons.DPadUp)
            || Pads[pad].ThumbSticks.Left.Y > .5f && beforePads[pad].ThumbSticks.Left.Y <= .5f;
        return (downPad ? 1 : 0) - (upPad ? 1 : 0);
    }

    public int? MenuDevice()
    {
        if (Press(Keys.Escape))
            return 0;
        for (int i = 0; i < ControllerCount; i++)
        {
            if (PadPress(i, Buttons.Start))
                return i + KeyboardCount;
        }

        return null;
    }

    public InputFrame Read(int device, bool consume = true)
    {
        if (device < 0)
        {
            return default;
        }

        var raw = ReadRaw(device);
        var result = raw with { Buttons = raw.Buttons | edges[device] };
        if (consume)
        {
            ClearPendingEdges(device);
        }

        return result;
    }

    private InputFrame ReadRaw(int device)
    {
        bool up;
        bool down;
        bool jump;
        bool attack;
        bool tongue;
        bool strafe;
        if (device < KeyboardCount)
        {
            var k = settings.Keyboard[device];
            up = KeysNow.IsKeyDown(k.Up);
            down = KeysNow.IsKeyDown(k.Down);
            jump = KeysNow.IsKeyDown(k.Jump);
            attack = KeysNow.IsKeyDown(k.Attack);
            tongue = KeysNow.IsKeyDown(k.Tongue);
            strafe = KeysNow.IsKeyDown(k.Strafe);
        }
        else
        {
            var p = Pads[device - KeyboardCount];
            if (!p.IsConnected)
                return default;
            var bindings = ControllerBindings(device - KeyboardCount);
            up = PadBindings.IsDown(p, bindings.Up);
            down = PadBindings.IsDown(p, bindings.Down);
            jump = PadBindings.IsDown(p, bindings.Jump);
            attack = PadBindings.IsDown(p, bindings.Attack);
            tongue = PadBindings.IsDown(p, bindings.Tongue);
            strafe = PadBindings.IsDown(p, bindings.Strafe);
        }

        return new(
            horizontal[device].Value,
            (sbyte)((up ? 1 : 0) - (down ? 1 : 0)),
            (jump ? InputButtons.Jump : 0)
                | (attack ? InputButtons.Attack : 0)
                | (tongue ? InputButtons.Tongue : 0)
                | (strafe ? InputButtons.Strafe : 0)
        );
    }

    private (bool Left, bool Right) HorizontalHeld(int device)
    {
        if (device < KeyboardCount)
        {
            var bindings = settings.Keyboard[device];
            return (KeysNow.IsKeyDown(bindings.Left), KeysNow.IsKeyDown(bindings.Right));
        }
        var pad = Pads[device - KeyboardCount];
        if (!pad.IsConnected)
            return (false, false);
        var padBindings = ControllerBindings(device - KeyboardCount);
        return (PadBindings.IsDown(pad, padBindings.Left), PadBindings.IsDown(pad, padBindings.Right));
    }

    private struct HorizontalInput
    {
        private bool leftHeld;
        private bool rightHeld;
        private long tick;
        private long leftPressedTick;
        private long rightPressedTick;
        public sbyte Value =>
            leftHeld && rightHeld
                ? (sbyte)rightPressedTick.CompareTo(leftPressedTick)
                : (sbyte)((rightHeld ? 1 : 0) - (leftHeld ? 1 : 0));

        public void AdvanceTick() => tick++;

        public void Update(bool left, bool right)
        {
            if (left && !leftHeld)
                leftPressedTick = tick;
            if (right && !rightHeld)
                rightPressedTick = tick;
            leftHeld = left;
            rightHeld = right;
        }
    }
}
