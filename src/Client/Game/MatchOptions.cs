using System.Text.Json.Serialization;
using FrogSmashers.Core;

namespace FrogSmashers.Client;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MatchOptions
{
    public GameRules Rules { get; }
    public uint Seed { get; }
    public bool ShuffleMaps { get; }

    public MatchOptions(GameRules rules, uint seed = 1, bool shuffleMaps = false)
    {
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Seed = seed;
        ShuffleMaps = shuffleMaps;
    }
}
