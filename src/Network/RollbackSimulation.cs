using FrogSmashers.Core;

namespace FrogSmashers.Network;

public interface IRollbackSimulation
{
    World World { get; }
    IReadOnlyList<SimulationEvent> Events => World.Events;
    byte[] Capture();
    byte[] Capture(out byte[] worldSnapshot);
    void Restore(byte[] snapshot);
    void Restore(byte[] snapshot, out byte[] worldSnapshot);
    void Tick(ReadOnlySpan<RollbackInput> inputs);
}

internal sealed class MatchSimulation : IRollbackSimulation
{
    private readonly int[] inputPlayerSlots;
    private readonly MatchInput[] commands;
    public World World { get; }

    public MatchSimulation(World world, int[] inputPlayerSlots)
    {
        World = world;
        this.inputPlayerSlots = inputPlayerSlots.ToArray();
        var humans = Enumerable.Range(0, world.Players.Length).Where(slot => !world.Rules.CpuPlayers[slot]);
        if (!inputPlayerSlots.Where(slot => slot >= 0).Order().SequenceEqual(humans))
            throw new ArgumentException("Every human frog needs one input stream");
        commands = new MatchInput[world.Players.Length];
    }

    public byte[] Capture() => World.Capture();

    public byte[] Capture(out byte[] worldSnapshot) => worldSnapshot = World.Capture();

    public void Restore(byte[] snapshot) => World.Restore(snapshot);

    public void Restore(byte[] snapshot, out byte[] worldSnapshot)
    {
        World.Restore(snapshot);
        worldSnapshot = snapshot;
    }

    public void Tick(ReadOnlySpan<RollbackInput> inputs)
    {
        if (inputs.Length != inputPlayerSlots.Length)
            throw new ArgumentException("Supply one input per human and host command stream");
        Array.Clear(commands);
        for (int handle = 0; handle < inputs.Length; handle++)
            if (inputPlayerSlots[handle] is int slot && slot >= 0)
                commands[slot] = new(inputs[handle].Gameplay, inputs[handle].Match);
        World.Advance(commands);
    }
}
