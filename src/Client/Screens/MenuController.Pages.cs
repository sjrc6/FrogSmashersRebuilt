using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    public IReadOnlyList<MenuEntry> Entries()
    {
        string OnOff(bool value) => value ? "ON" : "OFF";
        MenuEntry Link(string label, GameScreen screen) => new(label, () => Open(screen));
        MenuEntry BackRow() => new("BACK", Back);
        switch (Screen)
        {
            case GameScreen.Main:
                return
                [
                    new("LOCAL", () => ShowSeats()),
                    new("ONLINE", () => ShowSeats(true)),
                    Link("SETTINGS", GameScreen.Settings),
                    Link("EXTRAS", GameScreen.Extras),
                    new("QUIT", game.Exit),
                ];
            case GameScreen.Extras:
                return
                [
                    new(
                        "PLAY INTRO",
                        () =>
                        {
                            game.Cinematics.StartIntro();
                            Screen = GameScreen.Intro;
                        }
                    ),
                    new("WATCH CPUS", game.WatchCpus),
                    new("CREDITS", OpenCredits),
                    BackRow(),
                ];
            case GameScreen.Seats:
                return [];
            case GameScreen.LobbyMenu:
                var lobbyRows = new List<MenuEntry> { new("RETURN TO LOBBY", ReturnFromLobbyMenu) };
                if (game.Lobby.IsHost)
                {
                    lobbyRows.Add(new("START MATCH", StartFromSeats));
                    lobbyRows.Add(Link("MATCH SETTINGS", GameScreen.MatchSettings));
                    lobbyRows.Add(Link("EDIT SLOTS", GameScreen.SlotEditor));
                }
                lobbyRows.Add(Link("SETTINGS", GameScreen.Settings));
                foreach (
                    var player in game.Lobby.LocalPlayers.Where(player =>
                        !player.Cpu && (player.Id == Owner || Owner < 2 && player.Id < 2)
                    )
                )
                {
                    int device = player.Id;
                    string label = new LocalSeat(device).Label;
                    lobbyRows.Add(
                        new(
                            label + ": COLOR / TEAM",
                            () =>
                            {
                                game.Lobby.ChooseAgain(device);
                                ReturnFromLobbyMenu();
                            }
                        )
                    );
                    lobbyRows.Add(
                        new(
                            label + ": BACK OUT",
                            () =>
                            {
                                game.Lobby.BackOut(device);
                                ReturnFromLobbyMenu();
                            }
                        )
                    );
                }
                if (game.Online.Lobby is SteamLobby steamLobby)
                    lobbyRows.Add(new("INVITE FRIENDS", steamLobby.InviteFriends));
                if (game.Online.Lobby == null)
                    lobbyRows.Add(new("ONLINE OPTIONS", OpenOnline));
                else
                    lobbyRows.Add(new("LEAVE ONLINE LOBBY", game.ReturnToLobby));
                lobbyRows.Add(new("QUIT LOBBY", game.MainMenu, Color: new(255, 85, 85)));
                return lobbyRows;
            case GameScreen.SlotEditor:
                return [];
            case GameScreen.SlotOptions:
                var slot = game.Lobby.Roster.Slots[SelectedSeat];
                var roomRows = new List<MenuEntry>();
                if (slot.Player is { Cpu: false } occupant)
                    roomRows.Add(
                        new(
                            occupant.Peer == game.Lobby.LocalPeer ? "BACK OUT" : "KICK",
                            () => EditRoom(slot.Type, slot.Open, remove: true)
                        )
                    );
                roomRows.Add(
                    new(
                        "SLOT TYPE: " + (slot.Type == SlotType.Cpu ? "CPU" : slot.Type.ToString().ToUpperInvariant()),
                        Change: amount =>
                        {
                            EditRoom((SlotType)Wrap((int)slot.Type + amount, 3), slot.Open);
                            Selected = Entries().Count - 2;
                        }
                    )
                );
                roomRows.Add(new(slot.Open ? "CLOSE SLOT" : "OPEN SLOT", () => EditRoom(slot.Type, !slot.Open)));
                return roomRows;
            case GameScreen.Settings:
                var personal = new List<MenuEntry>
                {
                    new(
                        "BORDERLESS FULLSCREEN: " + OnOff(game.Settings.Fullscreen),
                        Change: _ =>
                        {
                            game.Settings.Fullscreen = !game.Settings.Fullscreen;
                            game.ApplyDisplay();
                        }
                    ),
                    new(
                        "VSYNC: " + OnOff(game.Settings.VSync),
                        Change: _ =>
                        {
                            game.Settings.VSync = !game.Settings.VSync;
                            game.ApplyDisplay();
                        }
                    ),
                    new(
                        "FRAME LIMIT: " + game.Settings.FrameLimit,
                        Change: amount =>
                            game.Settings.FrameLimit = FrameRates[
                                Wrap(Array.IndexOf(FrameRates, game.Settings.FrameLimit) + amount, FrameRates.Length)
                            ]
                    ),
                    new(
                        "VOLUME: " + (int)MathF.Round(game.Settings.Volume * 100) + "%",
                        Change: amount =>
                        {
                            game.Settings.Volume =
                                Math.Clamp((int)MathF.Round(game.Settings.Volume * 20) + amount, 0, 20) / 20f;
                            game.Audio.Volume = game.Settings.Volume;
                        }
                    ),
                    new(
                        "SCREEN SHAKE: " + OnOff(game.Settings.ScreenShake),
                        Change: _ =>
                        {
                            game.Settings.ScreenShake = !game.Settings.ScreenShake;
                            game.Renderer.ShakeEnabled = game.Cinematics.ShakeEnabled = game.Settings.ScreenShake;
                        }
                    ),
                    new(
                        "FONT SMOOTHING: " + new[] { "OFF", "NARROW", "NORMAL" }[game.Settings.FontSmoothing],
                        Change: amount =>
                        {
                            game.Settings.FontSmoothing = Wrap(game.Settings.FontSmoothing + amount, 3);
                            game.Renderer.TextEdgeWidth = game.FontEdgeWidth;
                        }
                    ),
                };
                if (KeyboardAllowed)
                {
                    personal.Add(new("KEYBOARD 1 CONTROLS", () => OpenBindings(0)));
                    personal.Add(new("KEYBOARD 2 CONTROLS", () => OpenBindings(1)));
                }
                personal.Add(BackRow());
                return personal;
            case GameScreen.MatchSettings:
                var entries = new List<MenuEntry>
                {
                    new("TEAMS: " + OnOff(Rules.TeamMode), Change: _ => Rules.TeamMode = !Rules.TeamMode),
                    new(
                        "WIN SCORE: " + (Rules.WinScore == 0 ? "ORIGINAL DEFAULT" : Rules.WinScore),
                        Change: amount => Rules.WinScore = Wrap(Rules.WinScore + amount, 31)
                    ),
                    new(
                        "MATCH ROUNDS: " + Rules.MatchRounds,
                        Change: amount => Rules.MatchRounds = Math.Clamp(Rules.MatchRounds + amount, 1, 20)
                    ),
                    new(
                        "FIRST ARENA: " + game.Assets.Data.Maps[Rules.FirstMap].Name,
                        Change: amount => Rules.FirstMap = Wrap(Rules.FirstMap + amount, 7)
                    ),
                    new(
                        "MAP ORDER: " + (Rules.ShuffleMaps ? "SHUFFLED" : "SEQUENTIAL"),
                        Change: _ => Rules.ShuffleMaps = !Rules.ShuffleMaps
                    ),
                };
                if (!Rules.TeamMode)
                    entries.Add(
                        new(
                            "CHARACTER BODY BOUNCES: " + OnOff(Rules.CharactersBounceEachOther),
                            Change: _ => Rules.CharactersBounceEachOther = !Rules.CharactersBounceEachOther
                        )
                    );
                entries.Add(BackRow());
                return entries;
            case GameScreen.Online:
                return
                [
                    Link("HOST STEAM LOBBY", GameScreen.HostSteam),
                    Link("JOIN STEAM LOBBY", GameScreen.JoinSteam),
                    Link("UDP / LAN", GameScreen.Udp),
                    BackRow(),
                ];
            case GameScreen.HostSteam:
                return
                [
                    Link("MATCH SETTINGS", GameScreen.MatchSettings),
                    Link("PLAYER SLOTS", GameScreen.SlotEditor),
                    new("OPEN PRIVATE STEAM LOBBY", () => game.BeginLobby("steam", true)),
                    BackRow(),
                ];
            case GameScreen.Udp:
                return
                [
                    Link("HOST UDP / LAN", GameScreen.HostUdp),
                    Link("JOIN BY ADDRESS", GameScreen.JoinUdp),
                    BackRow(),
                ];
            case GameScreen.HostUdp:
                return
                [
                    Link("MATCH SETTINGS", GameScreen.MatchSettings),
                    Link("PLAYER SLOTS", GameScreen.SlotEditor),
                    new("ALLOW LAN CONNECTIONS: " + OnOff(AllowLan), Change: _ => AllowLan = !AllowLan),
                    new("OPEN UDP LOBBY", () => game.BeginLobby("udp", true)),
                    BackRow(),
                ];
            case GameScreen.JoinSteam:
                return
                [
                    new("LOBBY ID: " + (SteamCode.Length == 0 ? "ENTER ID" : SteamCode), () => EditingAddress = true),
                    new("JOIN LOBBY", () => game.BeginLobby("steam:" + SteamCode, false)),
                    BackRow(),
                ];
            case GameScreen.JoinUdp:
                return
                [
                    new("ADDRESS: " + JoinAddress, () => EditingAddress = true),
                    new("JOIN LOBBY", () => game.BeginLobby("udp:" + JoinAddress, false)),
                    BackRow(),
                ];
            case GameScreen.Connecting:
                return game.Online.Lobby is SteamLobby steam
                    ? [new("INVITE STEAM FRIENDS", steam.InviteFriends), new("CANCEL", game.ReturnToLobby)]
                    : [new("CANCEL", game.ReturnToLobby)];
            case GameScreen.Playing:
                return game.Match.Paused
                    ?
                    [
                        new("RESUME", Resume),
                        Link("SETTINGS", GameScreen.Settings),
                        new("RETURN TO LOCAL LOBBY", game.ReturnToLobby),
                        new("MAIN MENU", game.MainMenu),
                    ]
                    : [];
            case GameScreen.Bindings:
                var keys = BindingKeys();
                string[] names = ["LEFT", "RIGHT", "UP", "DOWN", "JUMP", "BAT", "TONGUE", "STRAFE"];
                return names
                    .Select((name, i) => new MenuEntry(name, () => WaitingForBinding = true, Key: keys[i]))
                    .Append(BackRow())
                    .ToArray();
            case GameScreen.Error:
                return [new("MAIN MENU", game.MainMenu)];
            default:
                return [];
        }
    }
}
