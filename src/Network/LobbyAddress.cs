using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace FrogSmashers.Network;

public static class LobbyAddress
{
    public static bool TrySteam(string text, out ulong lobby)
    {
        lobby = 0;
        text = text.Trim();
        if (text.Length > 512)
            return false;
        if (text.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) && !text.Contains('/'))
            text = text[6..];
        if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out lobby))
            return lobby != 0;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Scheme == "steam" && uri.Host == "joinlobby" && uri.IsDefaultPort && uri.UserInfo.Length == 0)
        {
            if (parts.Length is < 2 or > 3 || parts[0] != "480")
                return false;
            return ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out lobby) && lobby != 0;
        }
        if (uri.Scheme == "https" && uri.Host == "steamcommunity.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0)
        {
            if (parts.Length != 3 || parts[0] != "lobby" || parts[1] != "480")
                return false;
            return ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out lobby) && lobby != 0;
        }
        return false;
    }

    public static bool TryUdp(string text, int defaultPort, out string host, out int port)
    {
        host = "";
        port = defaultPort;
        text = text.Trim();
        if (text.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
            text = text[6..];
        else if (text.StartsWith("udp:", StringComparison.OrdinalIgnoreCase))
            text = text[4..];
        if (
            text.Length is 0 or > 253
            || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_' or ':'))
        )
            return false;
        int separator = text.IndexOf(':');
        if (separator >= 0)
        {
            if (!int.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port))
                return false;
            text = text[..separator];
        }
        if (port is < 1 or > 65535 || text.Length == 0)
            return false;
        if (text.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            if (!IPAddress.TryParse(text, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                return false;
            text = address.ToString();
        }
        else if (text.Split('.').Any(label => label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-'))
            return false;
        host = text;
        return true;
    }
}
