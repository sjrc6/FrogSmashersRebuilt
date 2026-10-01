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

internal sealed class MatchSimulation(World world) : IRollbackSimulation
{
    private readonly InputFrame[] gameplay = new InputFrame[world.Players.Length];
    public World World => world;

    public byte[] Capture() => world.Capture();

    public void Restore(byte[] snapshot) => world.Restore(snapshot);

    public void Tick(ReadOnlySpan<RollbackInput> inputs)
    {
        for (int index = 0; index < inputs.Length; index++)
            gameplay[index] = inputs[index].Gameplay;
        world.Tick(gameplay);
    }
}
