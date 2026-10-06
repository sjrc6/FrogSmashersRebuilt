namespace FrogSmashers.Network;

public sealed record LobbyListing(
    string Id,
    string Target,
    string Name,
    int Players,
    int Capacity,
    int AvailableSlots,
    bool Compatible
)
{
    public static string DisplayName(string name, string fallback)
    {
        string value = new(name.Where(c => c is >= ' ' and <= '~').Take(24).ToArray());
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
    }
}

public interface ILobbyBrowser : IDisposable
{
    IReadOnlyList<LobbyListing> Results { get; }
    bool Searching { get; }
    string? Error { get; }
    void Refresh();
    void Poll();
}
