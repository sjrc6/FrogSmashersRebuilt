namespace FrogSmashers.Client;

internal sealed class MatchSetup
{
    public MatchPreferences Preferences { get; private set; } = new();
    public LocalLobby Lobby { get; } = new();
    public IReadOnlyList<LocalSeat> Seats => Lobby.Seats;
    public uint Seed { get; }
    public int FirstMap
    {
        get => Preferences.FirstMap;
        set => Preferences.FirstMap = value;
    }

    public MatchSetup(uint seed) => Seed = seed;

    public void ResetPreferences() => Preferences = new();

    public MatchOptions CreateOptions(int[]? customMapOrder = null)
    {
        var rules = Preferences.CreateRules();
        rules.PlayerCount = Seats.Count;
        rules.Teams = Enumerable
            .Range(0, 8)
            .Select(index => index < Seats.Count && Seats[index].Team >= 0 ? Seats[index].Team : index % 2)
            .ToArray();
        rules.Colors = Enumerable
            .Range(0, 8)
            .Select(index => index < Seats.Count ? Seats[index].Color : index)
            .ToArray();
        var order =
            customMapOrder?.ToArray()
            ?? (FirstMap == 6 ? [6] : Enumerable.Range(0, 6).Select(index => (index + FirstMap) % 6).ToArray());
        if (customMapOrder == null && Preferences.ShuffleMaps)
        {
            new Random((int)Seed).Shuffle(order.AsSpan(1));
        }

        rules.MapOrder = order;
        return new MatchOptions { Rules = rules, Seed = Seed };
    }
}
