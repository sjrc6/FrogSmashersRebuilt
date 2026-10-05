using FrogSmashers.Client;
using FrogSmashers.Core;
using FrogSmashers.Network;

internal static class MenuArchitectureTests
{
    public static void Run(Action<bool, string> check)
    {
        var selection = new MenuSelection();
        MenuEntry[] rows = [new("a", "SAME LABEL"), new("b", "SAME LABEL"), new("c", "BACK")];
        selection.Select(1);
        selection.Reconcile(GameScreen.BrowseLan, rows);
        selection.Reconcile(
            GameScreen.BrowseLan,
            [rows[2], rows[0], rows[1] with { Label = "RENAMED", Value = "2/8" }]
        );
        check(selection.Index == 2, "Dynamic menu selection follows identity through reorder, label and value changes");
        selection.Reconcile(GameScreen.BrowseLan, [rows[0], rows[2]]);
        check(selection.Index == 1, "Removed menu selection falls back to an existing row");
        selection.Restore(GameScreen.Settings, 0, "b");
        selection.Reconcile(GameScreen.Settings, rows);
        check(selection.Index == 1, "Back navigation restores an entry by identity");
        int actions = 0;
        var action = new MenuEntry(
            "delete",
            "REMOVE",
            () => actions++,
            Role: MenuRole.Destructive,
            DisabledReason: "HOST ONLY"
        );
        action.Select();
        check(
            actions == 0 && action.DisplayColor(false) == action.DisplayColor(true),
            "Disabled actions stay inactive and use the same disabled color"
        );
        var enabled = action with { DisabledReason = null };
        enabled.Select();
        check(
            actions == 1 && enabled.DisplayColor(false) != action.DisplayColor(false),
            "Enabled actions retain their semantic color"
        );
        check(
            new MenuEntry("start", "START", Role: MenuRole.Positive).DisplayColor(false) != enabled.DisplayColor(false),
            "Start and destructive actions have different semantic colors"
        );

        var setup = new MatchSetup(12, [new() { Id = "arena" }]);
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true), new(10, Team: 1, Color: 1, Spawned: true, Cpu: true)]);
        roster.SetPlayers(1, [new(0, 1, Team: 1, Color: 2, Spawned: true)]);
        var options = setup.CreateOptions(roster: roster);
        check(
            options.Rules.PlayerCount == 3 && options.Rules.CpuPlayers[1] && options.Rules.Colors[2] == 2,
            "Host freezes the entire mixed local, CPU and remote roster"
        );
        MatchSetup.ValidateRoster(options.Rules, roster);
        roster.RemovePeer(1);
        bool rejected = false;
        try
        {
            MatchSetup.ValidateRoster(options.Rules, roster);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        check(rejected, "A client rejects a different roster instead of rewriting host configuration");
        setup.Preferences.ChangeFormat(1);
        check(
            setup.Preferences.Format == MatchFormat.Teams && setup.Preferences.Scoring == ScoringMode.Points,
            "Team format and scoring are independent"
        );
        setup.Preferences.ChangeFormat(1);
        check(
            setup.Preferences.Format == MatchFormat.Crews && setup.Preferences.Scoring == ScoringMode.Stocks,
            "Choosing Crews enforces Stocks"
        );
        foreach (var definition in ModifierCatalog.All)
        {
            var modifiers = GameModifiers.Default;
            for (int change = 0; change < 300; change++)
            {
                modifiers = definition.Change(modifiers, change < 150 ? -1 : 1);
                modifiers.Validate();
            }
            check(
                definition.Help.Length > 0 && definition.Value(modifiers).Length > 0,
                "Modifier catalog provides help and bounded changes: " + definition.Id
            );
        }
        var teamRules = new GameRules(format: MatchFormat.Teams);
        check(
            ModifierCatalog.All.Single(d => d.Id == "body-bouncing").DisabledReason?.Invoke(teamRules) == null,
            "Body bouncing is available in teams"
        );
        var stocksRules = new GameRules(scoring: ScoringMode.Stocks);
        check(
            ModifierCatalog.All.Single(d => d.Id == "match-scoring").DisabledReason!(stocksRules) == "POINTS ONLY",
            "Cumulative scoring is visibly unavailable for Stocks"
        );
        Console.WriteLine("Menu identities, semantic actions and host configuration ownership passed");
    }
}
