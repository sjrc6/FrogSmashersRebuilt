using FrogSmashers.Core;

namespace FrogSmashers.Network;

public interface IRollbackSimulation
{
    World World { get; }
    IReadOnlyList<SimulationEvent> Events => World.Events;
    byte[] Capture();
    void Restore(byte[] snapshot);
    void Tick(ReadOnlySpan<RollbackInput> inputs);
}

internal sealed class MatchSimulation : IRollbackSimulation
{
    private readonly int[] inputPlayerSlots;
    private readonly InputFrame[] gameplay;
    public World World { get; }

    public MatchSimulation(World world, int[] inputPlayerSlots)
    {
        World = world;
        this.inputPlayerSlots = inputPlayerSlots.ToArray();
        var humans = Enumerable.Range(0, world.Players.Length).Where(slot => !world.Rules.CpuPlayers[slot]);
        if (!inputPlayerSlots.Where(slot => slot >= 0).Order().SequenceEqual(humans))
            throw new ArgumentException("Every human frog needs one input stream");
        gameplay = new InputFrame[world.Players.Length];
    }

    public byte[] Capture() => World.Capture();

    public void Restore(byte[] snapshot) => World.Restore(snapshot);

    public void Tick(ReadOnlySpan<RollbackInput> inputs)
    {
        if (inputs.Length != inputPlayerSlots.Length)
            throw new ArgumentException("Supply one input per human and host command stream");
        Array.Clear(gameplay);
        for (int handle = 0; handle < inputs.Length; handle++)
            if (inputPlayerSlots[handle] is int slot && slot >= 0)
                gameplay[slot] = inputs[handle].Gameplay;
        World.Tick(gameplay);
    }
}
