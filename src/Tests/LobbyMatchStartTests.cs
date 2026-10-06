using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void MatchSettingsUseRefreshedRoster()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0), new(1, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true), new(1, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Settings factory fixture did not connect");
        int creations = 0;
        string CreateSettings(LobbyRoster roster)
        {
            creations++;
            var current = host.Simulation.Membership.Rooms[0]!;
            Check(roster.Count == 4, "Settings factory receives the complete mixed-machine roster");
            Check(
                roster.Slots[0].Player == current && current.Spawned,
                "Settings factory runs after refreshing simulation-owned selections"
            );
            return "fresh settings";
        }
        Check(!host.Lobby.StartMatch(CreateSettings) && creations == 0, "An invalid start does not freeze settings");
        rig.Input = (node, _) => node == host ? new(default, (byte)LobbyInputActions.Spawn, ColorStep: 1) : default;
        rig.WaitFor(
            () => host.Simulation.Membership.Rooms[0]!.Spawned,
            "The host's spawn command did not reach simulation"
        );
        rig.Input = null;
        Check(!host.Lobby.Roster.Slots[0].Player!.Spawned, "The fixture captures a selection before roster refresh");
        Check(host.Lobby.StartMatch(CreateSettings) && creations == 1, "A valid start freezes settings exactly once");
        rig.WaitFor(() => host.Lobby.Ready && guest.Lobby.Ready, "Fresh settings failed to start");
        Check(guest.Lobby.MatchSettingsJson == "fresh settings", "Guests receive the same frozen settings");
    }

    private static void MatchSettingsFreezeBeforeCheckpoint()
    {
        using var rig = new Rig { Delay = 100 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Configuration freeze fixture did not connect");
        rig.Steps(100);
        Check(host.Lobby.StartMatch(_ => "frozen"), "Host can start a ready match");
        host.Lobby.SetMatchSettings("changed during preparation");
        Check(host.Lobby.MatchSettingsJson == "frozen", "Match settings freeze before the first checkpoint poll");
        Check(
            !host.Lobby.StartMatch(_ => "second start"),
            "A second start cannot overwrite the prepared configuration"
        );
        rig.WaitFor(() => host.Lobby.Ready && guest.Lobby.Ready, "Frozen configuration failed to commit");
        host.Lobby.SetMatchSettings("changed during match");
        guest.Lobby.SetMatchSettings("guest edit");
        rig.Steps(30);
        Check(
            host.Lobby.MatchSettingsJson == "frozen" && guest.Lobby.MatchSettingsJson == "frozen",
            "Host and guest retain the agreed configuration throughout the match"
        );
    }

    private static void PendingSelectionsCancelFrozenStart()
    {
        var map = TestFixtures.Map();
        map.Collision = map
            .Spawns.Select(spawn => new BoxData
            {
                X = spawn.X,
                Y = spawn.Y - .5m,
                Width = 3,
                Height = 1,
            })
            .ToList();
        using var rig = new Rig { Delay = 80 };
        var host = rig.Add("host", [new(0, Spawned: true)], map: map);
        var guest = rig.Add("guest", [new(0, Spawned: true)], map: map);
        guest.Lobby.SetRollbackSettings(new() { Delay = 2 });
        rig.WaitFor(() => rig.Ready, "Pending selection fixture did not connect");
        rig.Steps(200);
        int room = Array.FindIndex(
            guest.Simulation.Membership.Rooms.ToArray(),
            player => player?.Peer == guest.Lobby.LocalPeer
        );
        Check(LobbySimulation.OnStartingPlatform(guest.Simulation.World, room), "Guest can reopen color selection");
        int initialColor = guest.Simulation.Membership.Rooms[room]!.Color;
        rig.Input = (node, _) =>
            node == guest
                ? new(default, (byte)(LobbyInputActions.SelectColor | LobbyInputActions.Spawn), ColorStep: 1)
                : default;
        rig.Steps(1);
        Check(guest.Lobby.LobbySession!.LocalInputSubmitted, "A delayed color command was accepted before start");
        rig.Input = null;
        Check(host.Lobby.StartMatch(_ => "initial colors"), "Match start begins before the delayed selection arrives");
        rig.WaitFor(
            () => host.Lobby.Notice == "LOBBY CHANGED, TRY AGAIN" && rig.Ready,
            "A pending selection must cancel an outdated frozen roster"
        );
        Check(
            host.Lobby.Error == null && guest.Lobby.Error == null && !host.Lobby.Ready,
            "Cancelling an outdated start preserves the lobby connection"
        );
        Check(
            host.Simulation.Membership.Rooms[room]!.Color != initialColor,
            "The accepted selection survives cancellation"
        );
        Check(host.Lobby.StartMatch(_ => "updated colors"), "The host can retry with the confirmed roster");
        rig.WaitFor(() => host.Lobby.Ready && guest.Lobby.Ready, "Updated roster failed to start");
    }
}
