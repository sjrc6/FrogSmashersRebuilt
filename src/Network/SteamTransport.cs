namespace FrogSmashers.Network;

public enum SteamTransport
{
    Sockets,
    Legacy,
}

internal static class SteamTransportMetadata
{
    public static string Name(SteamTransport transport) =>
        transport switch
        {
            SteamTransport.Sockets => "sockets",
            SteamTransport.Legacy => "legacy-p2p",
            _ => throw new ArgumentOutOfRangeException(nameof(transport)),
        };

    public static SteamTransport Parse(string value) =>
        value switch
        {
            "sockets" => SteamTransport.Sockets,
            "legacy-p2p" => SteamTransport.Legacy,
            _ => throw new ArgumentException("Unsupported Steam lobby transport"),
        };
}
