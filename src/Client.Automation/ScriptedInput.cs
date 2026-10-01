using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

/// <summary>Reproducible device input for UI and render regression captures.</summary>
public sealed class ScriptedInput
{
    public sealed class Range
    {
        public int From { get; set; }
        public int To { get; set; }
        public Keys[] Keys { get; set; } = [];
        public Dictionary<int, Buttons[]> Pads { get; set; } = [];
        public int? MouseX { get; set; }
        public int? MouseY { get; set; }
        public bool MouseDown { get; set; }
        public bool MouseRightDown { get; set; }
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

    public GamePadState Pad(int frame, int pad) =>
        new(
            Vector2.Zero,
            Vector2.Zero,
            0,
            0,
            ranges
                .Where(r => frame >= r.From && frame < r.To)
                .SelectMany(r => r.Pads.GetValueOrDefault(pad, []))
                .Distinct()
                .ToArray()
        );

    public MouseState Mouse(int frame)
    {
        var position = ranges.LastOrDefault(r => frame >= r.From && r.MouseX.HasValue);
        bool down = ranges.Any(r => frame >= r.From && frame < r.To && r.MouseDown);
        bool rightDown = ranges.Any(r => frame >= r.From && frame < r.To && r.MouseRightDown);
        return new MouseState(
            position?.MouseX ?? 0,
            position?.MouseY ?? 0,
            0,
            down ? ButtonState.Pressed : ButtonState.Released,
            ButtonState.Released,
            rightDown ? ButtonState.Pressed : ButtonState.Released,
            ButtonState.Released,
            ButtonState.Released
        );
    }
}
