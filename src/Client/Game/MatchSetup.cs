using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed class MatchSetup
{
    public MatchOptions Options { get; set; }
    public GameRules Rules => Options.Rules;
    public int FirstMap { get; set; }
    public bool ShuffleMaps { get; set; }
    public List<LocalSeat> Seats { get; } = [];

    public MatchSetup(ClientSettings settings, uint seed)
    {
        Options = new MatchOptions { Rules = settings.Rules, Seed = seed };
        FirstMap = settings.FirstMap;
        ShuffleMaps = settings.ShuffleMaps;
    }

    public void ConfigureRules(int[]? customMapOrder)
    {
        Rules.PlayerCount = Seats.Count;
        Rules.Teams = Enumerable
            .Range(0, 8)
            .Select(index => index < Seats.Count && Seats[index].Team >= 0 ? Seats[index].Team : index % 2)
            .ToArray();
        if (customMapOrder != null)
        {
            Rules.MapOrder = customMapOrder;
            return;
        }

        var order = FirstMap == 6 ? [6] : Enumerable.Range(0, 6).Select(index => (index + FirstMap) % 6).ToArray();
        if (ShuffleMaps)
        {
            new Random((int)Options.Seed).Shuffle(order);
        }

        Rules.MapOrder = order;
    }
}
