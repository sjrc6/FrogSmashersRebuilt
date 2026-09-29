using System.Text.Json.Serialization;

namespace FrogSmashers.Network;

[JsonConverter(typeof(JsonStringEnumConverter<LobbyMessageKind>))]
internal enum LobbyMessageKind
{
    [JsonStringEnumMemberName("none")]
    None,

    [JsonStringEnumMemberName("hello")]
    Hello,

    [JsonStringEnumMemberName("welcome")]
    Welcome,

    [JsonStringEnumMemberName("ack")]
    Acknowledge,

    [JsonStringEnumMemberName("start")]
    Start,

    [JsonStringEnumMemberName("started")]
    Started,

    [JsonStringEnumMemberName("error")]
    Error,
}
