using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class ContentSimulationTests
{
    public static void AuthoredMaps()
    {
        string? contentPath = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "ContentBuild", "content.json");
            if (File.Exists(candidate))
            {
                contentPath = candidate;
                break;
            }

            candidate = Path.Combine(dir.FullName, "Content", "content.json");
            if (File.Exists(candidate))
            {
                contentPath = candidate;
                break;
            }
        }

        Check(contentPath != null, "authored content manifest is present for map simulation checks");
        if (contentPath != null)
        {
            var content = GameContent.Load(contentPath);
            BotNavigationTests.DownSmash(content);
            AuthoredLobby(content);
            foreach (var map in content.Maps)
            {
                foreach (var box in map.Collision)
                {
                    var geometry = CreateWorld(
                        map: new()
                        {
                            Id = "authored-tongue",
                            Collision = [box],
                            KillBounds = new()
                            {
                                Left = -1000,
                                Right = 1000,
                                Bottom = -1000,
                                Top = 1000,
                            },
                        }
                    );
                    var shooter = geometry.Players[0];
                    geometry.Players[1].Alive = false;
                    geometry.Players[1].SpawnTicks = 1000;
                    shooter.OnGround = false;
                    shooter.Mode = CharacterMode.Tongue;
                    shooter.TonguePhase = TonguePhase.Extending;
                    shooter.WasBouncingBeforeTongue = true;
                    shooter.X = FromDecimal(box.OneWay ? box.X : box.X - box.Width / 2 - 5);
                    shooter.Y = FromDecimal(box.OneWay ? box.Y + box.Height / 2 + 3.5m : box.Y - 1.5m);
                    shooter.TongueX = box.OneWay ? 0 : 1;
                    shooter.TongueY = box.OneWay ? -1 : 0;
                    bool touched = false;
                    for (int tick = 0; tick < 16; tick++)
                    {
                        Step(geometry);
                        touched |= geometry.Events.Any(e => e.Kind == SimulationEventKind.TongueLatch);
                    }

                    Check(touched, $"authored map {map.Name}: tongue attaches to original collider {box.Name}");
                }

                var world = new World(map, new(playerCount: 8, winScore: 999), 321);
                int flySpawns = 0;
                for (int i = 0; i < 7200; i++)
                {
                    world.Advance(
                        world
                            .Players.Select(p => BotController.GetInput(world, p.Slot))
                            .Select(frame => new MatchInput(frame))
                            .ToArray()
                    );
                    flySpawns += world.Events.Count(e => e.Kind == SimulationEventKind.FlySpawn);
                }

                byte[] snapshot = world.Capture();
                world.Restore(snapshot);
                Check(
                    world.Capture().SequenceEqual(snapshot),
                    $"authored map {map.Name}: eight-player bot simulation and snapshot stress"
                );
                Check(
                    flySpawns > 0,
                    $"authored map {map.Name}: fly spawning and motion exercised during sustained play"
                );
            }
        }
    }

    private static void AuthoredLobby(GameContent content)
    {
        var map = content.PresentationScenes["Lobby"];
        Check(
            map.Collision.Count(box => box.OneWay && box.Name.StartsWith("Room")) == 8,
            "Lobby must have eight room platforms and an empty center"
        );
        bool SolidAt(decimal x, decimal y) =>
            map.Collision.Any(box =>
                !box.OneWay
                && x >= box.X - box.Width / 2
                && x <= box.X + box.Width / 2
                && y >= box.Y - box.Height / 2
                && y <= box.Y + box.Height / 2
            );
        foreach (decimal x in new[] { -145m / 15, 145m / 15 })
        foreach (decimal y in new[] { 10m, 0m, -10m })
        {
            Check(SolidAt(x, y), "An interior lobby wall has no collision");
            Check(!SolidAt(x, y - 50m / 15), "A lobby doorway was blocked");
        }
        Check(
            SolidAt(-30, 8) && SolidAt(30, 8) && SolidAt(0, 250m / 15) && SolidAt(0, -250m / 15),
            "Lobby outer walls must contain players on all four sides"
        );
        foreach (var box in map.Collision)
        {
            decimal[] edges =
            [
                (box.X - box.Width / 2) * 15,
                (box.X + box.Width / 2) * 15,
                (box.Y - box.Height / 2) * 15,
                (box.Y + box.Height / 2) * 15,
            ];
            Check(
                edges.All(edge => Math.Abs(edge - decimal.Round(edge)) < .0000001m),
                "Lobby collision is not aligned to background pixels"
            );
        }
        var rules = new GameRules(lobby: true, playerCount: 8, mapOrder: [0]);
        var world = new World([map], rules, 7, content.CharacterParameters);
        var inputs = new InputFrame[8];
        for (int tick = 0; tick < 120; tick++)
            world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        Check(world.Players.All(player => !player.Alive), "Lobby spawned unjoined players");
        for (int room = 0; room < 8; room++)
            world.SetLobbySlot(room, true, 7 - room);
        world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        for (int room = 0; room < 8; room++)
        {
            var player = world.Players[room];
            Check(
                player.Alive
                    && player.X == FromDecimal(map.Spawns[room].X)
                    && player.Y == FromDecimal(map.Spawns[room].Y),
                "Lobby player did not spawn in assigned room"
            );
            Check(player.ColorIndex == 7 - room, "Selected lobby color was lost on spawning");
        }
        for (int tick = 0; tick < 60; tick++)
            world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        Check(world.Players.All(player => player.OnGround), "Room platforms did not support all eight spawns");
        inputs[0] = new(0, -1, InputButtons.Jump);
        for (int tick = 0; tick < 30; tick++)
            world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        inputs[0] = default;
        for (int tick = 0; tick < 180; tick++)
            world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        Check(
            world.Players[0].OnGround && world.Players[0].Y < FromDecimal(map.Spawns[0].Y) - 5,
            "Dropping from a room platform did not land on a floor-gap platform"
        );
        world.SetLobbySlot(0, false, 2);
        world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        Check(
            !world.Players[0].Alive && (world.Match.Players[0].Participation != Participation.Active),
            "Backing out left an active frog"
        );
        world.SetLobbySlot(0, true, 2);
        world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        Check(
            world.Players[0].ColorIndex == 2 && world.Players[0].Y == FromDecimal(map.Spawns[0].Y),
            "Rejoining did not reset the frog to its room"
        );
        world.Players[0].Y = FromDecimal(map.KillBounds.Bottom) - 2;
        world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        for (int tick = 0; tick < 200; tick++)
            world.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
        Check(
            world.Players[0].Alive
                && world.Match.Players.All(player => player.Score == 0)
                && world.Match.Phase == MatchPhase.Playing
                && !world.Fly.Active,
            "Lobby KO did not respawn without scoring"
        );
        var copy = new World([map], rules, 7, content.CharacterParameters);
        copy.Restore(world.Capture());
        Check(
            copy.HashState() == world.HashState() && copy.Players[0].ColorIndex == 2,
            "Lobby snapshot lost colors or player membership"
        );
    }
}
