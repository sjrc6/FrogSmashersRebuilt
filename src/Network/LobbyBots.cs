using FrogSmashers.Core;

namespace FrogSmashers.Network;

public sealed class LobbyBots
{
    private readonly uint[] generations = new uint[LobbyRoster.MaxPlayers];
    private readonly uint[] bodyGenerations = new uint[LobbyRoster.MaxPlayers];
    private readonly uint[] targetGenerations = new uint[LobbyRoster.MaxPlayers];
    private readonly int[] targets = Enumerable.Repeat(-1, LobbyRoster.MaxPlayers).ToArray();

    public void Reset()
    {
        Array.Clear(generations);
        Array.Clear(bodyGenerations);
        Array.Clear(targetGenerations);
        Array.Fill(targets, -1);
    }

    public void ResetRoom(int room)
    {
        generations[room]++;
        targets[room] = -1;
        for (int other = 0; other < targets.Length; other++)
            if (targets[other] == room)
                targets[other] = -1;
    }

    public InputFrame Read(World world, int room)
    {
        int target = targets[room];
        if (target < 0)
            return default;
        if (
            !world.Players[room].Alive
            || !world.Players[target].Alive
            || bodyGenerations[room] != generations[room]
            || targetGenerations[room] != generations[target]
        )
        {
            targets[room] = -1;
            return default;
        }
        return BotController.GetInput(world, room, target);
    }

    public void Observe(World world, LobbyMembership membership)
    {
        foreach (var item in world.Events)
        {
            if (item.Kind == SimulationEventKind.Spawn && item.Player >= 0)
                ResetRoom(item.Player);
            if (
                item.Kind != SimulationEventKind.Hit
                || item.Player < 0
                || item.Other < 0
                || membership.Rooms[item.Player]?.Cpu != true
            )
                continue;
            targets[item.Player] = item.Other;
            bodyGenerations[item.Player] = generations[item.Player];
            targetGenerations[item.Player] = generations[item.Other];
        }
    }

    internal void RemapRooms(int[] oldRooms)
    {
        var oldGenerations = generations.ToArray();
        var oldBodies = bodyGenerations.ToArray();
        var oldTargets = targets.ToArray();
        var oldTargetGenerations = targetGenerations.ToArray();
        Reset();
        for (int room = 0; room < oldRooms.Length; room++)
        {
            int previous = oldRooms[room];
            if (previous < 0)
                continue;
            generations[room] = oldGenerations[previous];
            bodyGenerations[room] = oldBodies[previous];
            int oldTarget = oldTargets[previous];
            targets[room] = oldTarget < 0 ? -1 : Array.IndexOf(oldRooms, oldTarget);
            targetGenerations[room] = oldTargetGenerations[previous];
        }
    }

    internal void Write(BinaryWriter writer)
    {
        for (int room = 0; room < targets.Length; room++)
        {
            writer.Write(generations[room]);
            writer.Write(bodyGenerations[room]);
            writer.Write(targetGenerations[room]);
            writer.Write(targets[room]);
        }
    }

    internal static LobbyBots Read(BinaryReader reader)
    {
        var bots = new LobbyBots();
        for (int room = 0; room < bots.targets.Length; room++)
        {
            bots.generations[room] = reader.ReadUInt32();
            bots.bodyGenerations[room] = reader.ReadUInt32();
            bots.targetGenerations[room] = reader.ReadUInt32();
            bots.targets[room] = reader.ReadInt32();
            if (bots.targets[room] is < -1 or >= LobbyRoster.MaxPlayers)
                throw new InvalidDataException("Invalid lobby CPU target");
        }
        return bots;
    }
}
