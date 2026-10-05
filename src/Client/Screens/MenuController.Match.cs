using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private IReadOnlyList<MenuEntry> MatchRows()
    {
        var options = DisplayedMatchOptions();
        if (options == null)
            return [new("waiting", "WAITING FOR HOST", DisabledReason: "WAITING FOR HOST"), BackRow()];
        var rules = options.Rules;
        string? readOnly = MatchSettingsReadOnly;
        var entries = new List<MenuEntry>
        {
            new(
                "format",
                "FORMAT",
                Value: rules.Format == MatchFormat.Ffa ? "FFA" : rules.Format.ToString().ToUpperInvariant(),
                ValueSample: "TEAMS",
                Change: Rules.ChangeFormat,
                DisabledReason: readOnly
            ),
            new(
                "scoring",
                "SCORING",
                Value: rules.Scoring.ToString().ToUpperInvariant(),
                ValueSample: "POINTS",
                Change: _ =>
                    Rules.Scoring = Rules.Scoring == ScoringMode.Points ? ScoringMode.Stocks : ScoringMode.Points,
                DisabledReason: readOnly ?? (rules.Format == MatchFormat.Crews ? "CREWS REQUIRES STOCKS" : null)
            ),
            new(
                "target",
                rules.Scoring == ScoringMode.Points ? "ROUND TARGET" : "STARTING STOCKS",
                Value: rules.Scoring == ScoringMode.Stocks ? rules.StartingStocks.ToString()
                    : rules.WinScore == 0 ? "AUTO"
                    : rules.WinScore.ToString(),
                ValueSample: "AUTO",
                RepeatAdjust: true,
                DisabledReason: readOnly,
                Change: amount =>
                {
                    if (Rules.Scoring == ScoringMode.Stocks)
                        Rules.StartingStocks = Math.Clamp(Rules.StartingStocks + amount, 1, 30);
                    else
                        Rules.WinScore = Wrap(Rules.WinScore + amount, 31);
                }
            ),
            new(
                "rounds",
                "ROUNDS",
                Value: rules.MatchRounds.ToString(),
                ValueSample: "20",
                RepeatAdjust: true,
                Change: amount => Rules.MatchRounds = Math.Clamp(Rules.MatchRounds + amount, 1, 20),
                DisabledReason: readOnly ?? (rules.Format == MatchFormat.Crews ? "CREWS USES ONE LIFE POOL" : null)
            ),
            new(
                "first-arena",
                "FIRST ARENA",
                Value: game.Assets.Data.Maps[rules.MapOrder[0]].Name,
                ValueSample: LongestArenaName(),
                Change: game.Setup.ChangeFirstMap,
                DisabledReason: readOnly
            ),
            new(
                "map-order",
                "MAP ORDER",
                Value: options.ShuffleMaps ? "SHUFFLED" : "SEQUENTIAL",
                ValueSample: "SEQUENTIAL",
                Change: _ => Rules.ShuffleMaps = !Rules.ShuffleMaps,
                DisabledReason: readOnly
            ),
            BackRow(),
        };
        return entries;
    }

    private string LongestArenaName() =>
        game.Assets.Data.Maps.OrderByDescending(map => game.Assets.Font.Measure(map.Name).X).First().Name;
}
