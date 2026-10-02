using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private IReadOnlyList<MenuEntry> RollbackRows()
    {
        var lobby = game.Online.Lobby;
        var preferences = game.Settings.Rollback;
        var rows = new List<MenuEntry>();

        void Change(Func<RollbackPreferences, RollbackPreferences> update)
        {
            game.Settings.Rollback = update(game.Settings.Rollback).Normalize();
            lobby?.SetRollbackSettings(game.Settings.Rollback);
            game.Match.Network?.SetTiming(game.Settings.Rollback);
            game.ScheduleSettingsSave();
        }

        rows.Add(
            new(
                "DELAY",
                Value: RollbackPreferences.Milliseconds(preferences.Delay),
                RepeatAdjust: true,
                Change: amount => Change(value => value with { Delay = value.Delay + amount })
            )
        );
        rows.Add(
            new(
                "DONATION",
                Value: RollbackPreferences.Milliseconds(preferences.Donation),
                RepeatAdjust: true,
                Change: amount => Change(value => value with { Donation = value.Donation + amount })
            )
        );
        rows.Add(
            new(
                "MAX EXTRA DELAY",
                Value: RollbackPreferences.Milliseconds(preferences.MaxExtraDelay),
                RepeatAdjust: true,
                Change: amount => Change(value => value with { MaxExtraDelay = value.MaxExtraDelay + amount })
            )
        );
        rows.Add(new("BACK", Back));
        return rows;
    }
}
