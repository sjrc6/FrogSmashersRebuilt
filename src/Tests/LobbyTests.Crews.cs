using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void CrewStartAfterDeparture(bool sharedColor)
    {
        using var rig = new Rig { Delay = 30, Jitter = 10 };
        var host = rig.Add("host", [new(0, Spawned: !sharedColor)]);
        for (int peer = 1; peer < 5; peer++)
            rig.Add("guest" + peer, [new(0, Spawned: !sharedColor)]);
        rig.WaitFor(() => rig.Ready && host.Lobby.Roster.Count == 5, "Five-client lobby failed to synchronize");
        rig.Steps(100);
        var leaving = rig.Nodes[2];
        leaving.Lobby.Dispose();
        leaving.Active = false;
        rig.WaitFor(
            () => rig.Ready && host.Lobby.Roster.Count == 4,
            "Closing one of five clients did not settle the lobby"
        );
        host.Lobby.SetMatchSettings("{\"format\":\"crews\"}");
        rig.SharedColors = true;
        if (sharedColor)
        {
            var colorPeer = rig.Nodes[1];
            var ticks = rig
                .Nodes.Where(node => node.Active)
                .ToDictionary(node => node, node => node.Simulation.World.TickNumber + 6);
            rig.Input = (node, handle) =>
                node.Simulation.World.TickNumber == ticks[node]
                    ? new(default, (byte)LobbyInputActions.Spawn, ColorStep: node == colorPeer ? (sbyte)-1 : (sbyte)0)
                    : default;
        }
        rig.Steps(150);
        Check(
            host.Simulation.Membership.Rooms.OfType<LobbyPlayer>().All(player => player.Spawned),
            "Remaining crew players are all spawned"
        );
        Check(
            host.Lobby.StartMatch(_ => "{\"format\":\"crews\"}"),
            $"Crews can start after a departure (shared color: {sharedColor}); notice: {host.Lobby.Notice}"
        );
        rig.WaitFor(
            () => rig.Nodes.Where(node => node.Active).All(node => node.Lobby.Ready),
            "Crew start checkpoint failed after a departure"
        );
        Check(
            rig.Nodes.Where(node => node.Active)
                .All(node =>
                    node.Lobby.PlayerColors.SequenceEqual(host.Lobby.PlayerColors)
                    && node.Lobby.PlayerColors.Distinct().Count() == (sharedColor ? 3 : 4)
                ),
            "All remaining clients start with the chosen colors, including duplicates"
        );
    }

    private static void SharedCrewColorsSurviveMembershipChanges()
    {
        using var rig = new Rig
        {
            Delay = 45,
            Jitter = 25,
            Loss = .03,
            SharedColors = true,
        };
        var host = rig.Add("host", [new(0, Color: 0)]);
        var guest = rig.Add("guest", [new(0, Color: 1)]);
        rig.WaitFor(() => rig.Ready, "Shared crew colors did not synchronize");
        long colorTick = guest.Simulation.World.TickNumber + 6;
        rig.Input = (node, handle) =>
            node == guest && node.Simulation.World.TickNumber == colorTick ? new(default, ColorStep: -1) : default;
        rig.Steps(120);
        Check(
            rig.Nodes.All(node => node.Simulation.Membership.Rooms.Take(2).All(player => player!.Color == 0)),
            "Shared colors converge across peers under loss and jitter"
        );
        rig.Input = null;
        var observer = rig.Add("observer", []);
        rig.WaitFor(() => rig.Ready, "An observer could not join with shared crew colors");
        Check(observer.Simulation.SharedColors, "Spectator bootstrap retains the host color policy");
        Check(
            guest.Lobby.SetPlayers([.. guest.Lobby.Roster.Players(guest.Lobby.LocalPeer), new(1, Color: 7)]),
            "A guest can add another local player while sharing colors"
        );
        rig.WaitFor(() => rig.Ready && host.Lobby.Roster.Count == 3, "Shared-color party change did not synchronize");
        rig.Steps(120);
        Check(
            rig.Nodes.All(node => node.Simulation.Membership.Rooms.Take(2).All(player => player!.Color == 0)),
            "Party changes preserve incumbent shared colors for players and spectators"
        );
        rig.AssertConfirmedStates();
        rig.SharedColors = false;
        rig.Steps(180);
        Check(
            rig.Nodes.All(node =>
                !node.Simulation.SharedColors
                && node.Simulation.Membership.Rooms.OfType<LobbyPlayer>()
                    .Select(player => player.Color)
                    .Distinct()
                    .Count() == 3
            ),
            "Returning to unique colors reaches spectators through the host input stream"
        );
        rig.AssertConfirmedStates();
    }
}
