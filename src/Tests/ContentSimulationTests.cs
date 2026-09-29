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

                var world = new World(map, new() { PlayerCount = 8, WinScore = 999 }, 321);
                int flySpawns = 0;
                for (int i = 0; i < 7200; i++)
                {
                    world.Tick(world.Players.Select(p => BotController.GetInput(world, p.Slot)).ToArray());
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
}
