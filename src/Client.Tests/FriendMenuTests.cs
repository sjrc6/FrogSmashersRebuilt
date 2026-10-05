using FrogSmashers.Client;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Steamworks;

internal static class FriendMenuTests
{
    public static void Run(Action<bool, string> check)
    {
        SteamFriend Friend(
            ulong id,
            string name,
            EPersonaState state,
            bool inGame = false,
            bool current = false,
            bool lobby = false
        ) => new(id, name, state, inGame, current, lobby);
        SteamFriend[] friends =
        [
            Friend(1, "Offline", EPersonaState.k_EPersonaStateOffline),
            Friend(2, "Other game", EPersonaState.k_EPersonaStateOnline, true),
            Friend(3, "Zebra", EPersonaState.k_EPersonaStateOnline),
            Friend(4, "Alpha", EPersonaState.k_EPersonaStateOnline),
            Friend(5, "Current game", EPersonaState.k_EPersonaStateOnline, true, true),
            Friend(6, "Current lobby", EPersonaState.k_EPersonaStateOnline, true, true, true),
            Friend(7, "Away", EPersonaState.k_EPersonaStateAway),
            Friend(8, "Busy", EPersonaState.k_EPersonaStateBusy),
            Friend(9, "Looking to play", EPersonaState.k_EPersonaStateLookingToPlay),
        ];
        check(
            SteamFriend
                .Sort(friends, new HashSet<ulong>())
                .Select(friend => friend.Id)
                .SequenceEqual(new ulong[] { 6, 5, 4, 3, 2, 9, 8, 7, 1 }),
            "DGR invitation sorting preserves game/lobby/online weights, persona priority and alphabetical ties"
        );
        check(
            SteamFriend.Sort(friends, new HashSet<ulong> { 1 })[0].Id == 1,
            "Refreshing applies DGR's priority for previously invited friends"
        );
        check(friends[0].Presence == SteamFriendPresence.Offline, "Offline friends retain offline presence");
        check(friends[2].Presence == SteamFriendPresence.Online, "Online friends without games retain online presence");
        check(friends[1].Presence == SteamFriendPresence.InGame, "Any other game has its own presence");
        check(friends[4].Presence == SteamFriendPresence.InCurrentGame, "The running Steam app has distinct presence");
        check(
            FriendPresentation.Status(friends[1].Presence) == "IN-GAME"
                && FriendPresentation.Status(friends[4].Presence) == "IN-GAME"
                && FriendPresentation.StatusColor(friends[1].Presence)
                    != FriendPresentation.StatusColor(friends[4].Presence),
            "Current-app and other-game friends share IN-GAME text but have different status colors"
        );

        var settings = new ClientSettings();
        var keys = default(KeyboardState);
        var pad = default(GamePadState);
        var controls = new Controls(settings)
        {
            KeyboardSource = () => keys,
            MouseSource = () => default,
            GamePadSource = index => index == 0 ? pad : default,
            ControllerSource = _ => new("Friend menu test", "friend-test"),
        };
        controls.Poll();
        keys = new(settings.Keyboard[0].Attack);
        controls.Poll();
        check(
            FriendPresentation.RefreshDevice(controls, settings) == 0,
            "Refresh uses the keyboard gameplay attack binding"
        );
        controls.Poll();
        check(
            FriendPresentation.RefreshDevice(controls, settings) == null,
            "Holding refresh does not repeatedly reload friends"
        );
        keys = default;
        controls.Poll();
        settings.Keyboard[1].Attack = Keys.F7;
        keys = new(Keys.F7);
        controls.Poll();
        check(
            FriendPresentation.RefreshDevice(controls, settings) == 1,
            "Refresh recognizes a remapped second keyboard"
        );
        keys = default;
        pad = new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.None);
        controls.Poll();
        controls.ControllerBindings(0).Attack = Buttons.RightTrigger;
        pad = new(Vector2.Zero, Vector2.Zero, 0, 1, Buttons.None);
        controls.Poll();
        check(
            FriendPresentation.RefreshDevice(controls, settings) == 2,
            "Refresh follows remapped controller triggers"
        );
        controls.Poll();
        check(
            FriendPresentation.RefreshDevice(controls, settings) == null,
            "Held controller refresh is edge-triggered"
        );

        var layout = new MenuLayout.Measurement(new(0, 0, 560, 600), [], 0, [0, 6, 12]);
        var repeat = new MenuRepeat();
        check(
            MenuLayout.PageSelection(layout, repeat.Read(1, 1, 0)) == 6,
            "Right selects the next friend page immediately"
        );
        layout = layout with { Page = 1 };
        check(repeat.Read(1, 0, .34) == 0, "Paging pauses before repeating");
        check(
            MenuLayout.PageSelection(layout, repeat.Read(1, 0, .02)) == 12,
            "Holding right advances again without resetting on selection"
        );
        check(MenuLayout.PageSelection(layout with { Page = 2 }, 1) == 0, "Paging right wraps from the final page");
        check(MenuLayout.PageSelection(layout with { Page = 0 }, -1) == 12, "Paging left wraps from the first page");
        check(
            !MenuLayout.FooterRefresh(layout.Panel).Intersects(MenuLayout.FooterBack(layout.Panel)),
            "Refresh and Back footer click areas cannot overlap"
        );
    }
}
