using Steamworks;

namespace FrogSmashers.Network;

public enum SteamFriendPresence
{
    Offline,
    Online,
    InGame,
    InCurrentGame,
}

public sealed record SteamFriend(
    ulong Id,
    string Name,
    EPersonaState PersonaState,
    bool InGame,
    bool InCurrentGame,
    bool InLobby
)
{
    public SteamFriendPresence Presence =>
        InCurrentGame ? SteamFriendPresence.InCurrentGame
        : InGame ? SteamFriendPresence.InGame
        : PersonaState == EPersonaState.k_EPersonaStateOffline ? SteamFriendPresence.Offline
        : SteamFriendPresence.Online;

    private int InvitePriority =>
        (
            InCurrentGame ? -4
            : InGame ? 1
            : 0
        )
        - (InLobby ? 1 : 0)
        - (PersonaState == EPersonaState.k_EPersonaStateOnline ? 2 : 0);

    private int PersonaPriority =>
        PersonaState switch
        {
            EPersonaState.k_EPersonaStateLookingToPlay => 0,
            EPersonaState.k_EPersonaStateOnline => 1,
            EPersonaState.k_EPersonaStateLookingToTrade => 2,
            EPersonaState.k_EPersonaStateBusy => 3,
            EPersonaState.k_EPersonaStateAway => 4,
            EPersonaState.k_EPersonaStateSnooze => 5,
            _ => 6,
        };

    public static SteamFriend[] Sort(IEnumerable<SteamFriend> friends, IReadOnlySet<ulong> invited) =>
        friends
            .OrderBy(friend => friend.InvitePriority - (invited.Contains(friend.Id) ? 8 : 0))
            .ThenBy(friend => friend.PersonaPriority)
            .ThenBy(friend => friend.Name)
            .ThenBy(friend => friend.Id)
            .ToArray();
}

public sealed record SteamAvatar(int Image, int Width, int Height, byte[] Pixels);
