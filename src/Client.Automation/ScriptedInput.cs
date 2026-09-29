using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

/// <summary>Reproducible keyboard input for UI and render regression captures.</summary>
public sealed class ScriptedInput
{
    public sealed class Range
    {
        public int From { get; set; }
        public int To { get; set; }
        public Keys[] Keys { get; set; } = [];
    }

    private readonly Range[] ranges;

    public ScriptedInput(string path)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        ranges = JsonSerializer.Deserialize<Range[]>(File.ReadAllText(path), options) ?? [];
        if (ranges.Any(r => r.From < 0 || r.To <= r.From || r.Keys == null))
        {
            throw new InvalidDataException("Input ranges require 0 <= From < To and Keys.");
        }
    }

    public KeyboardState State(int frame) =>
        new(ranges.Where(r => frame >= r.From && frame < r.To).SelectMany(r => r.Keys).Distinct().ToArray());
}
