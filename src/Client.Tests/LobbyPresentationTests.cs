using FrogSmashers.Client;
using FrogSmashers.Core;
using FrogSmashers.Network;

internal static class LobbyPresentationTests
{
    public static void Run(Action<bool, string> check)
    {
        var committed = new LobbyRoster();
        committed.SetPlayers(0, [new(0, Spawned: true)]);
        var proposed = new LobbyRoster();
        proposed.Replace(committed.Slots);
        proposed.SetPlayers(1, [new(2, Color: 1)]);
        var view = new LobbyPresentation();
        view.Update(committed, proposed);
        check(
            view.Pending && view.Status(1) == "JOINING..." && view.Roster.Slots[1].Player is { Id: 2, Spawned: false },
            "Pending join displays the frog and progress before admission completes"
        );
        check(committed.Count == 1, "Join feedback cannot mutate simulation membership");
        check(
            view.AnnounceJoin(1, 2) && !view.AnnounceJoin(1, 2),
            "Repeated presses cannot duplicate the joining effect"
        );
        var preview = new SimulationEvent(300, 0, SimulationEventKind.LobbyPreview, 1, 1, 0, 0, 0);
        check(
            !view.ShowEvent(preview, proposed.Slots[1].Player),
            "Committed preview does not repeat immediate join feedback"
        );
        check(
            !view.ShowEvent(preview with { Sequence = 3 }, proposed.Slots[1].Player),
            "Rollback cannot replay join feedback under another event index"
        );
        var color = preview with { Tick = 301 };
        check(view.ShowEvent(color, proposed.Slots[1].Player), "Later color changes retain their effects");
        check(
            view.PlaySound(color) && !view.PlaySound(color with { Sequence = 4 }),
            "Confirmation and rollback cannot replay local feedback audio"
        );
        view.Update(proposed, proposed);
        check(!view.Pending, "Admission completion removes pending join status");
        var leaving = new LobbyRoster();
        leaving.Replace(proposed.Slots);
        leaving.SetSpectating(1, true);
        view.Update(proposed, leaving);
        check(
            view.Status(1) == "SPECTATING..." && view.Roster.Slots[1].Player == null,
            "Backing out shows progress and removes the static room marker immediately"
        );
        view.Update(leaving, leaving);
        check(!view.Pending, "Completed spectating clears room progress");
        view.Update(leaving, proposed);
        check(
            view.Status(1) == "JOINING..." && view.Roster.Spectator(1) == null,
            "A returning spectator gets the same pending join preview"
        );
        view.Update(leaving, leaving);
        check(!view.Pending && view.Roster.Spectator(1) != null, "Rejected admission restores the spectator display");

        committed.SetPlayers(0, [new(0, Color: 1)]);
        view.Update(committed, proposed);
        check(
            view.Roster.Slots[0].Player!.Color != view.Roster.Slots[1].Player!.Color,
            "Pending preview resolves colors against current predicted cosmetics"
        );
        view.Clear();
        check(!view.Pending && view.AnnounceJoin(1, 2), "Leaving the lobby clears feedback state");
    }
}
