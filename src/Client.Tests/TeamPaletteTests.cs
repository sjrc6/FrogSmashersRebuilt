using FrogSmashers.Client;
using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;

internal static class TeamPaletteTests
{
    public static void Run(string contentRoot, Action<bool, string> check)
    {
        var content = GameContent.Load(Path.Combine(contentRoot, "content.json"));
        for (int team = 0; team < 8; team++)
        {
            for (int count = 1; count <= 4; count++)
            {
                var rooms = new LobbyPlayer?[8];
                for (int member = 0; member < count; member++)
                    rooms[member * 2] = new(member, Team: team, Color: 7 - member);
                var shades = Enumerable
                    .Range(0, count)
                    .Select(member => PlayerPalette.Lobby(rooms, member * 2, true))
                    .ToArray();
                check(shades.Distinct().Count() == count, "Every teammate has a distinct shade");
                check(shades[^1] == PlayerPalette.Colors[team], "Lowest color identity retains the base team color");
                check(shades.All(color => color.A == 255), "Team shades stay fully opaque");
                if (count > 1)
                    check(
                        shades.All(shade =>
                            shade.R <= Color.Lerp(PlayerPalette.Colors[team], Color.White, .25f).R
                            && shade.G <= Color.Lerp(PlayerPalette.Colors[team], Color.White, .25f).G
                            && shade.B <= Color.Lerp(PlayerPalette.Colors[team], Color.White, .25f).B
                        ),
                        "Team shades preserve color instead of washing teammates out to white"
                    );
                var players = rooms.OfType<LobbyPlayer>().Reverse().ToArray();
                var rules = new GameRules(
                    playerCount: count + 1,
                    format: MatchFormat.Teams,
                    teams: [.. Enumerable.Range(0, 8).Select(slot => slot < count ? team : (team + 1) % 8).ToArray()],
                    colors:
                    [
                        .. Enumerable.Range(0, 8).Select(slot => slot < count ? players[slot].Color : slot).ToArray(),
                    ]
                );
                var world = new World(content.Maps[0], rules);
                for (int slot = 0; slot < count; slot++)
                {
                    int room = Array.FindIndex(rooms, player => player?.Id == players[slot].Id);
                    check(
                        PlayerPalette.For(world, slot) == PlayerPalette.Lobby(rooms, room, true),
                        "Starting a match and reordering rooms retain teammate shades"
                    );
                    check(
                        PlayerPalette.Lobby(rooms, room, false) == PlayerPalette.Colors[players[slot].Color],
                        "Free-for-all lobby colors remain unchanged"
                    );
                }
                rules = new GameRules(playerCount: rules.PlayerCount, teams: rules.Teams, colors: rules.Colors);
                world = new World(content.Maps[0], rules);
                for (int slot = 0; slot < count; slot++)
                    check(
                        PlayerPalette.For(world, slot) == PlayerPalette.Colors[players[slot].Color],
                        "Free-for-all match colors remain unchanged"
                    );
            }
        }
    }
}
