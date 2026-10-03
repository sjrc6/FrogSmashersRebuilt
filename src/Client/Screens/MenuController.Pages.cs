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
                    new("ONLINE", OpenOnline),
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
                var lobbyRows = new List<MenuEntry>();
                if (game.Lobby.IsHost)
                {
                    lobbyRows.Add(new("START MATCH", StartFromSeats));
                    lobbyRows.Add(Link("MATCH SETTINGS", GameScreen.MatchSettings));
                    lobbyRows.Add(Link("EDIT SLOTS", GameScreen.SlotEditor));
                }
                lobbyRows.Add(Link("SETTINGS", GameScreen.Settings));
                lobbyRows.Add(Link("VIEW PLAYERS", GameScreen.ViewPlayers));
                if (game.Online.Lobby is SteamLobby steamLobby)
                    lobbyRows.Add(new("INVITE FRIENDS", steamLobby.InviteFriends));
                if (game.Online.Lobby == null)
                    lobbyRows.Add(new("ONLINE OPTIONS", OpenOnline));
                lobbyRows.Add(new("QUIT LOBBY", game.MainMenu, Color: new(255, 85, 85)));
                return lobbyRows;
            case GameScreen.SlotEditor:
                return [];
            case GameScreen.ViewPlayers:
                return PlayerRows();
            case GameScreen.Settings:
                var personal = new List<MenuEntry>
                {
                    new(
                        "FULLSCREEN",
                        Value: OnOff(game.Settings.Fullscreen),
                        Change: _ =>
                        {
                            game.Settings.Fullscreen = !game.Settings.Fullscreen;
                            game.ApplyDisplay();
                        }
                    ),
                    new(
                        "VSYNC",
                        Value: OnOff(game.Settings.VSync),
                        Change: _ =>
                        {
                            game.Settings.VSync = !game.Settings.VSync;
                            game.ApplyDisplay();
                        }
                    ),
                    new(
                        "FPS LIMIT",
                        RepeatAdjust: true,
                        Value: game.Settings.FrameLimit.ToString(),
                        DisabledReason: game.Settings.VSync ? "VSYNC ENABLED" : null,
                        Change: amount =>
                            game.Settings.FrameLimit = FrameRates[
                                Wrap(Array.IndexOf(FrameRates, game.Settings.FrameLimit) + amount, FrameRates.Length)
                            ]
                    ),
                    new(
                        "VOLUME",
                        RepeatAdjust: true,
                        Value: (int)MathF.Round(game.Settings.Volume * 100) + "%",
                        Change: amount =>
                        {
                            game.Settings.Volume =
                                Math.Clamp((int)MathF.Round(game.Settings.Volume * 20) + amount, 0, 20) / 20f;
                            game.Audio.Volume = game.Settings.Volume;
                        }
                    ),
                };
                if (ShowingMenuBackground)
                    personal.Add(
                        new(
                            "TITLE VOLUME",
                            RepeatAdjust: true,
                            Value: (int)MathF.Round(game.Settings.TitleVolume * 100) + "%",
                            Change: amount =>
                            {
                                game.Settings.TitleVolume =
                                    Math.Clamp((int)MathF.Round(game.Settings.TitleVolume * 20) + amount, 0, 20) / 20f;
                                game.Audio.TitleVolume = game.Settings.TitleVolume;
                            }
                        )
                    );
                personal.Add(
                    new(
                        "SCREEN SHAKE",
                        Value: OnOff(game.Settings.ScreenShake),
                        Change: _ =>
                        {
                            game.Settings.ScreenShake = !game.Settings.ScreenShake;
                            game.Renderer.ShakeEnabled = game.Cinematics.ShakeEnabled = game.Settings.ScreenShake;
                        }
                    )
                );
                personal.Add(new("CONTROLS", () => OpenBindings(HintDevice)));
                personal.Add(Link("ROLLBACK", GameScreen.Rollback));
                personal.Add(BackRow());
                return personal;
            case GameScreen.Rollback:
                return RollbackRows();
            case GameScreen.MatchSettings:
                var entries = new List<MenuEntry>
                {
                    new("TEAMS", Value: OnOff(Rules.TeamMode), Change: _ => Rules.TeamMode = !Rules.TeamMode),
                    new(
                        "ROUND TARGET",
                        RepeatAdjust: true,
                        Value: Rules.WinScore == 0 ? "AUTO" : Rules.WinScore.ToString(),
                        Change: amount => Rules.WinScore = Wrap(Rules.WinScore + amount, 31)
                    ),
                    new(
                        "ROUNDS",
                        RepeatAdjust: true,
                        Value: Rules.MatchRounds.ToString(),
                        Change: amount => Rules.MatchRounds = Math.Clamp(Rules.MatchRounds + amount, 1, 20)
                    ),
                    new(
                        "FIRST ARENA",
                        Value: game.Assets.Data.Maps[Rules.FirstMap].Name,
                        Change: amount => Rules.FirstMap = Wrap(Rules.FirstMap + amount, 7)
                    ),
                    new(
                        "MAP ORDER",
                        Value: Rules.ShuffleMaps ? "SHUFFLED" : "SEQUENTIAL",
                        Change: _ => Rules.ShuffleMaps = !Rules.ShuffleMaps
                    ),
                };
                entries.Add(BackRow());
                return entries;
            case GameScreen.Online:
                return
                [
                    Link("CREATE LOBBY", GameScreen.CreateLobby),
                    Link("JOIN LOBBY", GameScreen.JoinLobby),
                    BackRow(),
                ];
            case GameScreen.CreateLobby:
                return
                [
                    new("TYPE", Value: Creation.TypeLabel, Change: Creation.CycleType),
                    new(
                        "MAX PLAYERS",
                        RepeatAdjust: true,
                        Value: Creation.Capacity(game.Lobby.Roster).ToString(),
                        Change: amount => Creation.ChangeCapacity(amount, game.Lobby.Roster)
                    ),
                    new("CREATE LOBBY", CreateOnlineLobby),
                    BackRow(),
                ];
            case GameScreen.JoinLobby:
                return
                [
                    Link("BROWSE STEAM LOBBIES", GameScreen.BrowseSteam),
                    new("JOIN CLIPBOARD LOBBY", JoinClipboardLobby),
                    Link("BROWSE LAN LOBBIES", GameScreen.BrowseLan),
                    Link("JOIN UDP", GameScreen.JoinUdp),
                    BackRow(),
                ];
            case GameScreen.BrowseSteam or GameScreen.BrowseLan:
                return browserEntries.Select(row => row.Entry).ToArray();
            case GameScreen.JoinUdp:
                return
                [
                    new("ADDRESS", BeginAddressEdit, Value: JoinAddress),
                    new("JOIN LOBBY", () => game.BeginLobby("udp:" + JoinAddress, false)),
                    new("JOIN CLIPBOARD ADDRESS", JoinClipboardAddress),
                    BackRow(),
                ];
            case GameScreen.Connecting:
                return [new("CANCEL", game.CancelConnection)];
            case GameScreen.Playing:
                if (!game.Match.Paused)
                    return [];
                var pauseRows = new List<MenuEntry>
                {
                    Link("SETTINGS", GameScreen.Settings),
                    Link("VIEW PLAYERS", GameScreen.ViewPlayers),
                };
                if (game.Online.Lobby == null || game.Online.Lobby.IsHost)
                    pauseRows.Add(new("RETURN TO LOBBY", game.ReturnToLobby));
                pauseRows.Add(new("QUIT GAME", game.MainMenu, Color: new(255, 85, 85)));
                return pauseRows;
            case GameScreen.Bindings:
                string[] names = ["LEFT", "RIGHT", "UP", "DOWN", "JUMP", "BAT", "TONGUE", "STRAFE"];
                return names
                    .Select((name, i) => BindingEntry(name, i))
                    .Prepend(new MenuEntry(BindingTitle, Change: CycleBindingDevice, IsTitle: true))
                    .Append(new MenuEntry("RESET TO DEFAULT", ResetBindings))
                    .Append(BackRow())
                    .ToArray();
            default:
                return [];
        }
    }
}
