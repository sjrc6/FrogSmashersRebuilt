using FrogSmashers.Core;
using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed class LobbyBots
{
    private readonly PlayerState?[] bodies = new PlayerState?[8];
    private readonly PlayerState?[] attackers = new PlayerState?[8];

    public void Reset()
    {
        Array.Clear(bodies);
        Array.Clear(attackers);
    }

    public InputFrame Read(World world, int room)
    {
        var body = world.Players[room];
        var attacker = attackers[room];
        if (bodies[room] != body || attacker == null || !attacker.Alive || world.Players[attacker.Slot] != attacker)
        {
            bodies[room] = body;
            attackers[room] = null;
            return default;
        }
        return BotController.GetInput(world, room, attacker.Slot);
    }

    public void Observe(World world, LobbyRoster roster)
    {
        foreach (var item in world.Events)
        {
            if (item.Kind != SimulationEventKind.Hit || item.Player < 0 || item.Other < 0)
                continue;
            if (roster.Slots[item.Player].Player?.Cpu != true)
                continue;
            bodies[item.Player] = world.Players[item.Player];
            attackers[item.Player] = world.Players[item.Other];
        }
    }
}
