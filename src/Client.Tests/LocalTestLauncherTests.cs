using FrogSmashers.Client;
using Microsoft.Xna.Framework;

internal static class LocalTestLauncherTests
{
    public static void Run(Action<bool, string> check)
    {
        check(
            LaunchOptions.Parse(["--host", "udp"]).LocalPlayers == 0
                && LaunchOptions.Parse(["--join", "steam:123"]).LocalPlayers == 0,
            "Direct host and invite launches do not guess a player's controller"
        );
        check(
            LaunchOptions.Parse(["--join", "udp:localhost", "--local-players", "0"]).LocalPlayers == 0,
            "Explicit empty parties are accepted"
        );
        var host = LaunchOptions.Parse(["--localtest", "4", "--tile", "--port", "25199", "--map", "a map"]);
        check(
            host.Host == "udp" && host.Join == null && host.LocalTestCount == 4 && host.Tile,
            "Local test includes the host in its instance count"
        );
        var native = LocalTestLauncher.StartInfo(host, 2, "/game folder/FrogSmashersRebuilt.exe", "");
        check(
            native.FileName == "/game folder/FrogSmashersRebuilt.exe" && !native.UseShellExecute,
            "Published builds relaunch their executable without shell quoting"
        );
        var guest = LaunchOptions.Parse(native.ArgumentList.ToArray());
        check(
            guest.Join == "udp:127.0.0.1" && guest.Host == null && guest.LocalTestIndex == 2,
            "Child instances join instead of spawning their own hosts"
        );
        check(
            guest.Port == 25199 && guest.Tile && guest.Map == "a map" && guest.LocalTestCount == 4,
            "Guests retain port, tiling and arguments containing spaces"
        );
        var managed = LocalTestLauncher.StartInfo(host, 1, "/usr/bin/dotnet", "/game folder/FrogSmashersRebuilt.dll");
        check(
            managed.ArgumentList[0] == "/game folder/FrogSmashersRebuilt.dll",
            "Source builds relaunch dotnet with the game assembly"
        );
        check(LaunchOptions.Parse(["--localtest", "1"]).Host == "udp", "One-instance local test opens a host lobby");
        foreach (
            string[] arguments in new string[][]
            {
                ["--localtest", "0"],
                ["--localtest", "9"],
                ["--localtest"],
                ["--tile"],
                ["--localtest", "4", "--slots", "2"],
                ["--localtest", "5", "--local-players", "2"],
                ["--localtest", "2", "--join", "udp:127.0.0.1"],
                ["--localtest", "2", "--host", "steam"],
                ["--localtest", "2", "--record", "test.fsr"],
                ["--localtest", "2", "--replay", "test.fsr"],
                ["--localtest-instance", "1"],
                ["--localtest", "2", "--localtest-instance", "2"],
            }
        )
        {
            bool rejected = false;
            try
            {
                LaunchOptions.Parse(arguments);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            check(rejected, "Reject invalid launch: " + string.Join(" ", arguments));
        }
        foreach (
            var area in new Rectangle[] { new(0, 0, 1920, 1040), new(0, 0, 3840, 2120), new(-1280, 40, 1280, 984) }
        )
            for (int count = 1; count <= 8; count++)
            {
                var tiles = Enumerable
                    .Range(0, count)
                    .Select(index => LocalTestLauncher.TileBounds(area, count, index))
                    .ToArray();
                check(tiles.All(area.Contains), "Every test window stays inside the desktop work area");
                check(tiles.All(tile => tile.Width * 9 == tile.Height * 16), "Tiles preserve 16:9 rendering");
                check(tiles.All(tile => tile.Size == tiles[0].Size), "All test windows have equal dimensions");
                for (int index = 0; index < count; index++)
                    check(
                        tiles.Skip(index + 1).All(other => !tiles[index].Intersects(other)),
                        "Tiled windows never overlap"
                    );
            }
    }
}
