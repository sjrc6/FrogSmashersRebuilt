using FrogSmashers.Core;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed class Controls
{
    private const int KeyboardCount = 2;
    private const int ControllerCount = 8;
    private const float StickDeadZone = 0.3f;
    private readonly ClientSettings settings;
    public KeyboardState KeysNow { get; private set; }
    public KeyboardState KeysBefore { get; private set; }
    public GamePadState[] Pads { get; } = new GamePadState[ControllerCount];

    private readonly GamePadState[] beforePads = new GamePadState[ControllerCount];
    private readonly HorizontalInput[] horizontal = new HorizontalInput[KeyboardCount + ControllerCount];
    private readonly InputButtons[] held = new InputButtons[KeyboardCount + ControllerCount];
    private readonly InputButtons[] edges = new InputButtons[KeyboardCount + ControllerCount];
    public Func<KeyboardState>? KeyboardSource { get; set; }
    public Func<int, GamePadState>? GamePadSource { get; set; }
    public Func<MouseState>? MouseSource { get; set; }
    public MouseState MouseNow { get; private set; }
    private MouseState mouseBefore;
    private bool mouseHoverSuspended;
    public bool MouseMoved => !mouseHoverSuspended && MouseNow.Position != mouseBefore.Position;
    public bool MousePressed =>
        MouseNow.LeftButton == ButtonState.Pressed && mouseBefore.LeftButton == ButtonState.Released;

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

    public void Poll()
    {
        KeysBefore = KeysNow;
        KeysNow = KeyboardSource?.Invoke() ?? Keyboard.GetState();
        mouseBefore = MouseNow;
        MouseNow = MouseSource?.Invoke() ?? Mouse.GetState();
        if (MouseNow.Position == mouseBefore.Position)
            mouseHoverSuspended = false;
        for (int i = 0; i < ControllerCount; i++)
        {
            beforePads[i] = Pads[i];
            Pads[i] = GamePadSource?.Invoke(i) ?? GamePad.GetState(i, GamePadDeadZone.None);
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
            up = p.DPad.Up == ButtonState.Pressed || p.ThumbSticks.Left.Y > StickDeadZone;
            down = p.DPad.Down == ButtonState.Pressed || p.ThumbSticks.Left.Y < -StickDeadZone;
            jump = p.IsButtonDown(Buttons.A);
            attack = p.IsButtonDown(Buttons.X);
            tongue = p.IsButtonDown(Buttons.B);
            strafe = p.IsButtonDown(Buttons.LeftShoulder);
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
        return (
            pad.DPad.Left == ButtonState.Pressed || pad.ThumbSticks.Left.X < -StickDeadZone,
            pad.DPad.Right == ButtonState.Pressed || pad.ThumbSticks.Left.X > StickDeadZone
        );
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
