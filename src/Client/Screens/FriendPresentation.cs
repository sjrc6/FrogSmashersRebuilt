using FrogSmashers.Network;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal static class FriendPresentation
{
    public static readonly Color OnlineColor = new(115, 235, 130);

    public static string Status(SteamFriendPresence presence) =>
        presence switch
        {
            SteamFriendPresence.Offline => "OFFLINE",
            SteamFriendPresence.Online => "ONLINE",
            _ => "IN-GAME",
        };

    public static Color StatusColor(SteamFriendPresence presence) =>
        presence switch
        {
            SteamFriendPresence.Offline => Color.Gray,
            SteamFriendPresence.Online => OnlineColor,
            SteamFriendPresence.InGame => new Color(100, 175, 255),
            SteamFriendPresence.InCurrentGame => new Color(255, 130, 200),
            _ => throw new ArgumentOutOfRangeException(nameof(presence)),
        };

    public static int? RefreshDevice(Controls controls, ClientSettings settings)
    {
        for (int device = 0; device < 2; device++)
            if (controls.Press(settings.Keyboard[device].Attack))
                return device;
        for (int pad = 0; pad < controls.Pads.Length; pad++)
            if (controls.Pads[pad].IsConnected && controls.BindingPress(pad, controls.ControllerBindings(pad).Attack))
                return pad + 2;
        return null;
    }
}
