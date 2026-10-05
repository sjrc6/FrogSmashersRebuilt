using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private MatchOptions? DisplayedMatchOptions() =>
        game.Lobby.IsHost ? game.Setup.CreateOptions(game.Options.MapOrder, game.Lobby.Roster) : game.Lobby.HostOptions;

    private string? MatchSettingsReadOnly =>
        !game.Lobby.IsHost ? "HOST ONLY"
        : game.Online.Lobby?.Starting == true ? "MATCH IN PROGRESS"
        : game.Lobby.RosterUpdating ? "LOBBY UPDATING"
        : null;

    private IReadOnlyList<MenuEntry> ModifierRows()
    {
        var options = DisplayedMatchOptions();
        if (options == null)
            return [new("waiting", "WAITING FOR HOST", DisabledReason: "WAITING FOR HOST")];
        var rules = options.Rules;
        var entries = ModifierCatalog
            .All.Select(definition => new MenuEntry(
                definition.Id,
                definition.Label,
                Value: definition.Value(rules.Modifiers),
                ValueSample: definition.ValueSample,
                Help: definition.Help,
                DisabledReason: MatchSettingsReadOnly ?? definition.DisabledReason?.Invoke(rules),
                Change: amount => game.Setup.SetModifiers(definition.Change(Rules.Modifiers, amount)),
                RepeatAdjust: definition.Id is "fly-min" or "fly-max"
            ))
            .ToList();
        entries.Add(
            new(
                "reset-modifiers",
                "RESET MODIFIERS",
                () => game.Setup.SetModifiers(GameModifiers.Default),
                Role: MenuRole.Destructive,
                DisabledReason: MatchSettingsReadOnly,
                Help: "RESTORE DEFAULT MODIFIERS, INCLUDING PHYSICS FIXES ON AND BODY BOUNCING OFF."
            )
        );
        return entries;
    }
}
