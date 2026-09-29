namespace FrogSmashers.Core;

public sealed class TimelineData
{
    public float Duration { get; set; }
    public float SampleRate { get; set; }
    public bool Loop { get; set; }
    public List<TimelineTrackData> Tracks { get; set; } = new();
    public List<TimelineEventData> Events { get; set; } = new();
}

public sealed class TimelineTrackData
{
    public string ObjectPath { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Attribute { get; set; } = "";
    public List<TimelineKeyData> Keys { get; set; } = new();
}

public sealed class TimelineKeyData
{
    public float Time { get; set; }
    public string SpriteId { get; set; } = "";
    public float[] Value { get; set; } = [];
    public float[] InSlope { get; set; } = [];
    public float[] OutSlope { get; set; } = [];
    public bool Stepped { get; set; }
}

public sealed class TimelineEventData
{
    public float Time { get; set; }
    public string Function { get; set; } = "";
    public string Argument { get; set; } = "";
    public float Float { get; set; }
    public int Integer { get; set; }
}
