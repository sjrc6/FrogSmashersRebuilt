using FrogSmashers.Client;
using FrogSmashers.Core;
using FrogSmashers.Network;

internal static class LobbyTests
{
    public static void Run(string contentRoot, Action<bool, string> check)
    {
        var content = GameContent.Load(Path.Combine(contentRoot, "content.json"));
        var map = content.PresentationScenes["Lobby"];
        var world = new World([map], new GameRules { Lobby = true, PlayerCount = 8 }, 1, content.CharacterParameters);
        var inputs = new InputFrame[8];
        for (int room = 0; room < 8; room++)
            world.SetLobbySlot(room, true, room);
        for (int tick = 0; tick < 20; tick++)
            world.Tick(inputs);
        for (int room = 0; room < 8; room++)
        {
            var player = world.Players[room];
            var spawn = map.Spawns[room];
            check(LobbyLayout.OnStartingPlatform(world, room), $"room {room}: spawned frog stands on its own platform");
            player.OnGround = false;
            check(!LobbyLayout.OnStartingPlatform(world, room), $"room {room}: airborne frog cannot choose colors");
            player.OnGround = true;
            player.Y -= 5;
            check(!LobbyLayout.OnStartingPlatform(world, room), $"room {room}: floor below is not its platform");
            player.Y = Fixed.FromDecimal(spawn.Y);
            player.X = Fixed.FromDecimal(map.Spawns.First(other => other.X != spawn.X).X);
            check(!LobbyLayout.OnStartingPlatform(world, room), $"room {room}: another platform does not qualify");
            player.X = Fixed.FromDecimal(spawn.X);
            player.Alive = false;
            check(!LobbyLayout.OnStartingPlatform(world, room), $"room {room}: inactive frogs cannot choose colors");
            player.Alive = true;
        }
        inputs[0] = new(0, 0, InputButtons.Jump);
        world.Tick(inputs);
        check(!LobbyLayout.OnStartingPlatform(world, 0), "jumping removes the color selection prompt");
        inputs[0] = default;
        for (int tick = 0; tick < World.TickRate * 3; tick++)
            world.Tick(inputs);
        check(LobbyLayout.OnStartingPlatform(world, 0), "landing back on the platform restores the prompt");
        VerifyDoorways(content, map, check);
        VerifyBots(content, map, check);
        Console.WriteLine("Lobby platform landing and color-selection eligibility passed");
    }

    private static void VerifyBots(GameContent content, MapData map, Action<bool, string> check)
    {
        var world = new World([map], new GameRules { Lobby = true, PlayerCount = 8 }, 1, content.CharacterParameters);
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true)]);
        roster.Edit(1, SlotType.Cpu);
        roster.SetPlayers(0, [.. roster.Players(0), new(1, Spawned: true)]);
        for (int room = 0; room < 3; room++)
            world.SetLobbySlot(room, true, room);
        var inputs = new InputFrame[8];
        world.Tick(inputs);
        var bots = new LobbyBots();
        check(bots.Read(world, 1) == default, "Lobby CPU stays peaceful without being attacked");
        world.Players[0].X = world.Players[1].X - 4;
        world.Players[0].Y = world.Players[1].Y;
        world.Players[0].Facing = 1;
        for (int room = 0; room < 3; room++)
            world.Players[room].SpawnTicks = 0;
        bool hit = false;
        for (int tick = 0; tick < 15 && !hit; tick++)
        {
            inputs[0] = tick < 3 ? new(0, 0, InputButtons.Attack) : default;
            world.Tick(inputs);
            bots.Observe(world, roster.Membership());
            hit = world.Events.Any(item => item.Kind == SimulationEventKind.Hit && item.Player == 1 && item.Other == 0);
        }
        check(hit, "Lobby retaliation fixture delivers a real bat hit");
        world.Players[0].X = world.Players[1].X - 10;
        world.Players[0].Y = world.Players[1].Y;
        world.Players[2].X = world.Players[1].X + 1;
        world.Players[2].Y = world.Players[1].Y;
        check(bots.Read(world, 1).X == -1, "Provoked CPU pursues its attacker instead of a closer innocent player");
        world.SetLobbySlot(0, false, 0);
        world.SetLobbySlot(0, true, 0);
        check(bots.Read(world, 1) == default, "Attacker respawn clears lobby retaliation");
        bots.Observe(world, roster.Membership());
        world.SetLobbySlot(1, false, 1);
        world.SetLobbySlot(1, true, 1);
        check(bots.Read(world, 1) == default, "CPU respawn clears lobby retaliation");
    }

    private static void VerifyDoorways(GameContent content, MapData map, Action<bool, string> check)
    {
        foreach (var wall in map.Collision.Where(box => box.Name == "Room wall"))
        {
            decimal floor = map
                .Collision.Where(box =>
                    box.Width > box.Height
                    && box.X - box.Width / 2 <= wall.X
                    && box.X + box.Width / 2 >= wall.X
                    && box.Y + box.Height / 2 < wall.Y - wall.Height / 2
                )
                .Max(box => box.Y + box.Height / 2);
            foreach (sbyte direction in new sbyte[] { -1, 1 })
            {
                var world = new World(
                    [map],
                    new GameRules { Lobby = true, PlayerCount = 8 },
                    1,
                    content.CharacterParameters
                );
                world.SetLobbySlot(0, true, 0);
                var inputs = new InputFrame[8];
                world.Tick(inputs);
                var player = world.Players[0];
                player.X = Fixed.FromDecimal(wall.X - direction * (wall.Width / 2 + 1.1m));
                player.Y = Fixed.FromDecimal(floor);
                player.VX = direction * 20;
                inputs[0] = new(direction, 0, InputButtons.None);
                for (int tick = 0; tick < 30; tick++)
                    world.Tick(inputs);
                check(
                    direction * (player.X - Fixed.FromDecimal(wall.X)) > Fixed.FromDecimal(wall.Width / 2 + 1),
                    $"lobby doorway at {wall.X}, {floor}: frog passes through toward {direction} (position {player.X}, {player.Y}, velocity {player.VX}, {player.VY})"
                );

                player.X = Fixed.FromDecimal(wall.X - direction * (wall.Width / 2 + 1.1m));
                player.Y = Fixed.FromDecimal(wall.Y);
                player.VX = direction * 20;
                player.VY = 0;
                world.Tick(inputs);
                var stop = Fixed.FromDecimal(wall.X - direction * (wall.Width / 2 + 1));
                check(
                    Fixed.Abs(player.X - stop) < Fixed.FromDecimal(.00001m),
                    $"lobby wall at {wall.X}, {wall.Y}: blocks the frog above its opening toward {direction}"
                );
            }
        }
    }
}
