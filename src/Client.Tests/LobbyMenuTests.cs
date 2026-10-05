using FrogSmashers.Client;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

internal static class LobbyMenuTests
{
    public static void Run(Action<bool, string> check)
    {
        var gesture = new SlotEditGesture();
        check(gesture.Update(new(true, false, 0, 0, true)) == 0, "Slot type waits for action release");
        check(gesture.Update(new(false, false, 0, 0, false, true)) == 1, "Released action cycles forward once");
        gesture.Update(new(true, false, 0, 0, true));
        check(gesture.Update(new(false, false, 0, -1, true)) == -1, "Held action plus left cycles backward");
        check(gesture.Update(new(false, false, 0, 1, true)) == 1, "Held action plus right cycles forward");
        check(
            gesture.Update(new(false, false, 0, 0, false, true)) == 0,
            "Release after adjustment does not cycle again"
        );
        gesture.Reset();
        check(
            gesture.Update(new(false, false, 0, 0, false, true)) == 0,
            "Opening the editor cannot consume an earlier press"
        );

        KeyboardState keyboard = default;
        GamePadState pad = default;
        var controls = new Controls(new ClientSettings())
        {
            KeyboardSource = () => keyboard,
            GamePadSource = i => i == 0 ? pad : default,
            MouseSource = () => default,
        };
        keyboard = new(Keys.Enter);
        controls.Poll();
        check(
            MenuInput.Read(controls, 0) is { Accept: true, AcceptHeld: true, AcceptReleased: false },
            "Keyboard supplies held action state"
        );
        keyboard = default;
        controls.Poll();
        check(MenuInput.Read(controls, 0).AcceptReleased, "Keyboard supplies release edge");
        keyboard = new(Keys.U);
        controls.Poll();
        check(
            MenuInput.Read(controls).Remove && ButtonGlyph.Remove(0) == ButtonGlyph.Key(Keys.U),
            "U removes a slot occupant and matches the hint"
        );
        keyboard = new(Keys.Delete);
        controls.Poll();
        check(!MenuInput.Read(controls).Remove, "Delete is no longer a slot action");
        keyboard = default;
        pad = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A | Buttons.X | Buttons.Y);
        controls.Poll();
        check(
            MenuInput.Read(controls, 2) is { Accept: true, AcceptHeld: true, Remove: true, ApplyAll: true },
            "Controller supplies every slot edit action"
        );
        pad = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.None);
        controls.Poll();
        check(
            MenuInput.Read(controls, 2).AcceptReleased && !MenuInput.Read(controls, 0).AcceptReleased,
            "Controller supplies a release without a keyboard release"
        );
        keyboard = new(Keys.Enter);
        controls.Poll();
        pad = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.A);
        controls.Poll();
        keyboard = default;
        controls.Poll();
        check(
            MenuInput.Read(controls).AcceptHeld && !MenuInput.Read(controls).AcceptReleased,
            "One device releasing cannot end another device's held action"
        );
        pad = default;
        controls.Poll();
        check(MenuInput.Read(controls).AcceptReleased, "Shared action releases once every device has let go");

        var creation = new LobbyCreation();
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0), new(1), new(2)]);
        creation.Reset(roster);
        for (int i = 0; i < 10; i++)
            creation.ChangeCapacity(-1, roster);
        check(creation.Capacity(roster) == 3, "Max Players cannot displace existing locals");
        creation.Apply(roster);
        check(roster.Count == 3 && roster.Capacity == 3, "Creating online retains the local party");
        creation.CycleType(-1);
        check(creation.SlotType == SlotType.Friend && !creation.Lan, "Friends creation selects friend admission");
        creation.CycleType(1);
        check(creation.SlotType == SlotType.Open, "Public creation selects open admission");
        creation.CycleType(1);
        check(creation.Lan && creation.SlotType == SlotType.Open, "LAN creation opens ordinary remote slots");
        creation.CycleType(1);
        check(
            !creation.Lan && creation.Privacy == LobbyPrivacy.PrivateCode && creation.SlotType == SlotType.Private,
            "Private Code selects a secret policy independently of private room access"
        );
        creation.SelectPrivate();
        check(
            creation.Privacy == LobbyPrivacy.Private && creation.TypeLabel == "PRIVATE",
            "Sharing from local play creates ordinary Private lobbies"
        );
        Console.WriteLine("Lobby creation and shared slot actions passed");
    }
}
