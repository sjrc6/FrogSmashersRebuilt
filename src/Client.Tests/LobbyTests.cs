using FrogSmashers.Client;
using FrogSmashers.Core;

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
            player.Y -= 7;
            check(!LobbyLayout.OnStartingPlatform(world, room), $"room {room}: floor below is not its platform");
            player.Y = Fixed.FromDecimal(spawn.Y);
            player.X += 29;
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
        Console.WriteLine("Lobby platform landing and color-selection eligibility passed");
    }
}
