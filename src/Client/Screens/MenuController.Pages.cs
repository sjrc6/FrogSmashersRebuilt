using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private IReadOnlyList<MenuEntry> BuildEntries()
    {
        switch (Screen)
        {
            case GameScreen.Main:
                return
                [
                    new("local", "LOCAL", () => ShowSeats()),
                    new("online", "ONLINE", OpenOnline),
                    new("join-clipboard-lobby", "JOIN FROM CLIPBOARD", JoinClipboardLobby, Role: MenuRole.Positive),
                    Link("SETTINGS", GameScreen.Settings),
                    Link("EXTRAS", GameScreen.Extras),
                    new("quit", "QUIT", game.Exit, Role: MenuRole.Destructive),
                ];
            case GameScreen.Extras:
                return
                [
                    new(
                        "play-intro",
                        "PLAY INTRO",
                        () =>
                        {
                            game.Cinematics.StartIntro();
                            Screen = GameScreen.Intro;
                        }
                    ),
                    new("watch-cpus", "WATCH CPUS", game.WatchCpus),
                    new("credits", "CREDITS", OpenCredits),
                    BackRow(),
                ];
            case GameScreen.Seats:
                return [];
            case GameScreen.LobbyMenu:
                var lobbyRows = new List<MenuEntry>();
                if (game.Lobby.IsHost)
                {
                    lobbyRows.Add(
                        new(
                            "start-match",
                            "START MATCH",
                            StartFromSeats,
                            Role: MenuRole.Positive,
                            DisabledReason: MatchStartBlockedReason()
                        )
                    );
                }
                lobbyRows.Add(Link("MATCH SETTINGS", GameScreen.MatchSettings));
                lobbyRows.Add(Link("MODIFIERS", GameScreen.Modifiers));
                if (game.Lobby.IsHost)
                    lobbyRows.Add(Link("EDIT SLOTS", GameScreen.SlotEditor));
                lobbyRows.Add(Link("SETTINGS", GameScreen.Settings));
                lobbyRows.Add(Link("VIEW PLAYERS", GameScreen.ViewPlayers));
                if (game.Online.Lobby == null || game.Online.Lobby is SteamLobby)
                {
                    var steamLobby = game.Online.Lobby as SteamLobby;
                    lobbyRows.Add(
                        new(
                            "invite-friends",
                            "INVITE FRIENDS",
                            InviteFriends,
                            Role: MenuRole.Positive,
                            DisabledReason: steamLobby is { IsHost: false } ? "HOST ONLY - SHARE A CODE" : null
                        )
                    );
                    lobbyRows.Add(
                        new(
                            "copy-lobby-code",
                            "COPY LOBBY CODE",
                            CopyLobbyCode,
                            Role: MenuRole.Positive,
                            DisabledReason: steamLobby?.ShareCodeUnavailable
                        )
                    );
                    if (steamLobby is { IsHost: true, Privacy: LobbyPrivacy.PrivateCode })
                        lobbyRows.Add(new("change-code", "CHANGE CODE", ChangeLobbyCode, Role: MenuRole.Destructive));
                }
                if (game.Online.Lobby == null)
                    lobbyRows.Add(new("online-options", "ONLINE OPTIONS", OpenOnline));
                lobbyRows.Add(new("quit-lobby", "QUIT LOBBY", game.MainMenu, Role: MenuRole.Destructive));
                return lobbyRows;
            case GameScreen.SlotEditor:
                return [];
            case GameScreen.ViewPlayers:
                return PlayerRows();
            case GameScreen.Settings:
                return PersonalRows();
            case GameScreen.Rollback:
                return RollbackRows();
            case GameScreen.MatchSettings:
                return MatchRows();
            case GameScreen.Modifiers:
                return ModifierRows();
            case GameScreen.Online:
                return
                [
                    Link("CREATE LOBBY", GameScreen.CreateLobby),
                    Link("JOIN LOBBY", GameScreen.JoinLobby),
                    BackRow(),
                ];
            case GameScreen.CreateLobby:
                var creation = new List<MenuEntry>
                {
                    new(
                        "max-players",
                        "MAX PLAYERS",
                        RepeatAdjust: true,
                        Value: Creation.Capacity(game.Lobby.Roster).ToString(),
                        Change: amount => Creation.ChangeCapacity(amount, game.Lobby.Roster)
                    ),
                    new("type", "TYPE", Value: Creation.TypeLabel, Change: Creation.CycleType),
                };
                creation.Add(
                    Link("ADVANCED", GameScreen.CreateLobbyAdvanced) with
                    {
                        DisabledReason = Creation.Lan ? "STEAM LOBBIES ONLY" : null,
                    }
                );
                creation.Add(new("create-lobby", "CREATE LOBBY", CreateOnlineLobby));
                creation.Add(BackRow());
                return creation;
            case GameScreen.CreateLobbyAdvanced:
                return
                [
                    new(
                        "steam-api",
                        "STEAM API",
                        Value: game.Settings.SteamTransport == SteamTransport.Legacy ? "LEGACY" : "SOCKETS",
                        Change: _ =>
                        {
                            game.Settings.SteamTransport =
                                game.Settings.SteamTransport == SteamTransport.Legacy
                                    ? SteamTransport.Sockets
                                    : SteamTransport.Legacy;
                            game.ScheduleSettingsSave();
                        }
                    ),
                    BackRow(),
                ];
            case GameScreen.JoinLobby:
                return
                [
                    Link("BROWSE STEAM LOBBIES", GameScreen.BrowseSteam),
                    new("join-clipboard-lobby", "JOIN CLIPBOARD LOBBY", JoinClipboardLobby),
                    Link("BROWSE LAN LOBBIES", GameScreen.BrowseLan),
                    Link("JOIN UDP", GameScreen.JoinUdp),
                    BackRow(),
                ];
            case GameScreen.BrowseSteam or GameScreen.BrowseLan:
                return browserEntries;
            case GameScreen.InviteFriends:
                return FriendRows();
            case GameScreen.JoinUdp:
                return
                [
                    new("address", "ADDRESS", BeginAddressEdit, Value: JoinAddress),
                    new("join-lobby", "JOIN LOBBY", () => game.BeginLobby("udp:" + JoinAddress, false)),
                    new("join-clipboard-address", "JOIN CLIPBOARD ADDRESS", JoinClipboardAddress),
                    BackRow(),
                ];
            case GameScreen.Connecting:
                return [new("cancel", "CANCEL", game.CancelConnection)];
            case GameScreen.Playing:
                if (!game.Match.Paused)
                    return [];
                var pauseRows = new List<MenuEntry> { Link("SETTINGS", GameScreen.Settings) };
                if (game.Online.Lobby != null)
                    pauseRows.Add(Link("VIEW PLAYERS", GameScreen.ViewPlayers));
                if (game.Online.Lobby == null || game.Online.Lobby.IsHost)
                    pauseRows.Add(new("return-to-lobby", "RETURN TO LOBBY", game.ReturnToLobby));
                pauseRows.Add(new("quit-game", "QUIT GAME", game.MainMenu, Role: MenuRole.Destructive));
                return pauseRows;
            case GameScreen.Bindings:
                string[] names = ["LEFT", "RIGHT", "UP", "DOWN", "JUMP", "BAT", "TONGUE", "STRAFE"];
                return names
                    .Select((name, i) => BindingEntry(name, i))
                    .Prepend(new MenuEntry("binding-device", BindingTitle, Change: CycleBindingDevice, IsTitle: true))
                    .Append(
                        new MenuEntry("reset-to-default", "RESET TO DEFAULT", ResetBindings, Role: MenuRole.Destructive)
                    )
                    .Append(BackRow())
                    .ToArray();
            default:
                return [];
        }
    }
}
