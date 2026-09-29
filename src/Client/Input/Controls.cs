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
    private readonly InputButtons[] held = new InputButtons[KeyboardCount + ControllerCount];
    private readonly InputButtons[] edges = new InputButtons[KeyboardCount + ControllerCount];
    public Func<KeyboardState>? KeyboardSource { get; set; }

    public Controls(ClientSettings settings) => this.settings = settings;

    public void ClearPendingEdges() => Array.Clear(edges);

    public void Poll()
    {
        KeysBefore = KeysNow;
        KeysNow = KeyboardSource?.Invoke() ?? Keyboard.GetState();
        for (int i = 0; i < ControllerCount; i++)
        {
            beforePads[i] = Pads[i];
            Pads[i] = GamePad.GetState(i, GamePadDeadZone.None);
        }

        for (int i = 0; i < KeyboardCount + ControllerCount; i++)
        {
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
            edges[device] = 0;
        }

        return result;
    }

    private InputFrame ReadRaw(int device)
    {
        bool left;
        bool right;
        bool up;
        bool down;
        bool jump;
        bool attack;
        bool tongue;
        bool strafe;
        if (device < KeyboardCount)
        {
            var k = settings.Keyboard[device];
            left = KeysNow.IsKeyDown(k.Left);
            right = KeysNow.IsKeyDown(k.Right);
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
            left = p.DPad.Left == ButtonState.Pressed || p.ThumbSticks.Left.X < -StickDeadZone;
            right = p.DPad.Right == ButtonState.Pressed || p.ThumbSticks.Left.X > StickDeadZone;
            up = p.DPad.Up == ButtonState.Pressed || p.ThumbSticks.Left.Y > StickDeadZone;
            down = p.DPad.Down == ButtonState.Pressed || p.ThumbSticks.Left.Y < -StickDeadZone;
            jump = p.IsButtonDown(Buttons.A);
            attack = p.IsButtonDown(Buttons.X);
            tongue = p.IsButtonDown(Buttons.B);
            strafe = p.IsButtonDown(Buttons.LeftShoulder);
        }

        return new(
            (sbyte)((right ? 1 : 0) - (left ? 1 : 0)),
            (sbyte)((up ? 1 : 0) - (down ? 1 : 0)),
            (jump ? InputButtons.Jump : 0)
                | (attack ? InputButtons.Attack : 0)
                | (tongue ? InputButtons.Tongue : 0)
                | (strafe ? InputButtons.Strafe : 0)
        );
    }
}
