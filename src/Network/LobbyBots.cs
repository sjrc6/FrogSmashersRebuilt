using FrogSmashers.Core;

namespace FrogSmashers.Network;

public sealed class LobbyBots
{
    private readonly bool[] playing = new bool[LobbyRoster.MaxPlayers];

    public void Reset() => Array.Clear(playing);

    public void ResetRoom(int room) => playing[room] = false;

    public InputFrame Read(World world, int room) =>
        playing[room] ? BotController.BeachBallInput(world, room) : default;

    public void Observe(World world, LobbyMembership membership)
    {
        foreach (var item in world.Events)
        {
            if (item.Kind == SimulationEventKind.Spawn && item.Player >= 0)
                ResetRoom(item.Player);
            if (
                item.Kind == SimulationEventKind.LobbyContact
                && item.Player >= 0
                && membership.Rooms[item.Player]?.Cpu == true
                && item.Other >= 0
                && membership.Rooms[item.Other] is { Cpu: false }
            )
                playing[item.Player] = true;
        }
    }

    internal void RemapRooms(int[] oldRooms)
    {
        var previous = playing.ToArray();
        for (int room = 0; room < playing.Length; room++)
            playing[room] = oldRooms[room] >= 0 && previous[oldRooms[room]];
    }

    internal void Write(BinaryWriter writer)
    {
        foreach (bool active in playing)
            writer.Write(active);
    }

    internal static LobbyBots Read(BinaryReader reader)
    {
        var bots = new LobbyBots();
        for (int room = 0; room < bots.playing.Length; room++)
            bots.playing[room] = reader.ReadBoolean();
        return bots;
    }
}
