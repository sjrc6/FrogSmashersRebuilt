using FrogSmashers.Core;

namespace FrogSmashers.Client;

public sealed class MatchOptions
{
    public GameRules Rules { get; set; } = new();
    public uint Seed { get; set; } = 1;
}
