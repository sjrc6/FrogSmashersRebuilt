using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    public static void RunLive()
    {
        foreach (int delay in new[] { 16, 200 })
            LiveAdmission(delay);
        SlowLoadingDoesNotExpireHistory();
        RepeatedConnectionsAndRoles();
        MultipleDeparturesAndNewConnection();
        PromptDeparture(false);
        PromptDeparture(true);
        AbruptDeparture();
        foreach (int ticks in new[] { 0, 5, 20, 250 })
            FiveClientDeparture(ticks);
    }

    private static void SlowLoadingDoesNotExpireHistory()
    {
        using var rig = new Rig { Delay = 200 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("loading", [new(0)], attach: false);
        rig.Steps(720);
        Check(host.Simulation.World.TickNumber == 720, "Loading a newcomer cannot stop the running lobby");
        guest.Lobby.AttachSimulation(guest.Simulation);
        rig.WaitFor(() => rig.Ready, "Loading longer than retained history prevented admission");
        rig.Steps(100);
        rig.AssertConfirmedStates();
    }

    private static void RepeatedConnectionsAndRoles()
    {
        using var rig = new Rig
        {
            Delay = 80,
            Jitter = 16,
            Loss = .03,
            DatagramMode = true,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.Add("staying", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Reconnect fixture did not connect");
        var session = host.Lobby.LobbySession;
        for (int iteration = 0; iteration < 2; iteration++)
        {
            Check(guest.Lobby.SetSpectating(guest.Lobby.LocalPeer, true), "Repeated spectate was rejected");
            rig.WaitFor(() => rig.Ready, "Repeated spectate stalled");
            Check(guest.Lobby.SetSpectating(guest.Lobby.LocalPeer, false), "Repeated promotion was rejected");
            rig.WaitFor(() => rig.Ready, "Repeated promotion stalled");
            rig.Steps(80);
            rig.AssertConfirmedStates();
            int peer = guest.Lobby.LocalPeer;
            guest.Lobby.Dispose();
            guest.Active = false;
            rig.WaitFor(() => rig.Ready && !host.Lobby.PeerIds.Contains(peer), "Repeated departure stalled");
            guest = rig.Add("returning" + iteration, [new(0)]);
            rig.WaitFor(() => rig.Ready, "Reusing a disconnected machine slot stalled");
            Check(guest.Lobby.LocalPeer == peer, "Reconnect fixture did not reuse its machine slot");
            rig.Steps(120);
            rig.AssertConfirmedStates();
        }
        Check(
            ReferenceEquals(session, host.Lobby.LobbySession),
            "Role changes and reconnects replaced the running session"
        );
    }

    private static void FiveClientDeparture(int ticks)
    {
        using var rig = new Rig
        {
            Delay = 30,
            Jitter = 25,
            Loss = .05,
            DatagramMode = true,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        for (int peer = 1; peer < 5; peer++)
            rig.Add("guest" + peer, [new(0, Spawned: true)]);
        rig.WaitFor(
            () => host.Lobby.Roster.Count == 5 && rig.Nodes[2].Lobby.Connected,
            "Five-client departure fixture failed to connect"
        );
        rig.Steps(ticks);
        var leaving = rig.Nodes[2];
        leaving.Lobby.Dispose();
        leaving.Active = false;
        long departed = rig.Now;
        rig.WaitFor(() => rig.Ready && host.Lobby.Roster.Count == 4, "Five-client departure did not settle", 600);
        rig.Steps(200);
        rig.AssertConfirmedStates();
        Console.WriteLine($"Five-client departure after {ticks} ticks: {rig.Now - departed - 2000} ms to settle");
    }

    private static void PromptDeparture(bool hostLeaves)
    {
        using var rig = new Rig { Delay = 200, DatagramMode = true };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Departure fixture did not connect");
        rig.Steps(100);
        var leaving = hostLeaves ? host : guest;
        long before = rig.Now;
        guest.AllowError = hostLeaves;
        leaving.Lobby.Dispose();
        leaving.Active = false;
        rig.WaitFor(
            () => hostLeaves ? guest.Lobby.Error != null : host.Lobby.Roster.Count == 1,
            "Graceful departure waited for a timeout"
        );
        long detectedAfter = rig.Now - before;
        Check(detectedAfter <= 225, "Graceful departure should take only one network delivery");
        if (!hostLeaves)
        {
            rig.WaitFor(() => rig.Ready, "The remaining lobby did not recover from a graceful exit");
            long tick = host.Simulation.World.TickNumber;
            rig.Steps(60);
            Check(host.Simulation.World.TickNumber >= tick + 59, "A departed peer continued blocking play");
        }
        Console.WriteLine(
            $"Graceful {(hostLeaves ? "host" : "guest")} departure at 400 ms RTT: {detectedAfter} ms to detect"
        );
    }

    private static void AbruptDeparture()
    {
        using var rig = new Rig { Delay = 16 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Abrupt departure fixture did not connect");
        guest.Active = false;
        long started = rig.Now;
        rig.WaitFor(() => rig.Ready && host.Lobby.Roster.Count == 1, "Silent departure did not time out");
        Check(rig.Now - started <= 5200, "Silent departure exceeded the five-second fallback");
    }

    private static void MultipleDeparturesAndNewConnection()
    {
        using var rig = new Rig { Delay = 80 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var first = rig.Add("first", [new(0, Spawned: true)]);
        var second = rig.Add("second", [new(0, Spawned: true)]);
        rig.Add("survivor", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Concurrent departure fixture did not connect");
        rig.Steps(100);
        first.Active = second.Active = false;
        first.Lobby.Dispose();
        second.Lobby.Dispose();
        rig.WaitFor(() => rig.Ready && host.Lobby.PeerIds.Count == 2, "Concurrent departures stalled");
        rig.Add("newcomer", [new(0)]);
        rig.WaitFor(() => rig.Ready, "Historical disconnects prevented a fresh admission");
        rig.Steps(120);
        rig.AssertConfirmedStates();
    }

    private static void LiveAdmission(int delay)
    {
        using var rig = new Rig { Delay = delay };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        rig.Steps(120);
        var original = host.Lobby.LobbySession;
        long started = rig.Now;
        long before = host.Simulation.World.TickNumber;
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Live admission did not complete");
        Console.WriteLine(
            $"Live connection at {delay * 2} ms RTT: {rig.Now - started} ms; host advanced {host.Simulation.World.TickNumber - before} ticks"
        );
        Check(
            ReferenceEquals(original, host.Lobby.LobbySession),
            "Joining a connection retains the running host session"
        );
        rig.Steps(160);
        rig.AssertConfirmedStates();
        started = rig.Now;
        Check(
            guest.Lobby.SetPlayers([new(0, guest.Lobby.LocalPeer), new(1, guest.Lobby.LocalPeer)]),
            "Add second local frog"
        );
        rig.WaitFor(() => rig.Ready, "Party admission did not complete");
        Console.WriteLine($"Live room join at {delay * 2} ms RTT: {rig.Now - started} ms");
        Check(rig.Now - started < delay * 2 + 150, "Room admission should need approximately one RTT");
        rig.Steps(160);
        rig.AssertConfirmedStates();
        Check(guest.Lobby.RemovePlayer(guest.Lobby.LocalPeer, 1), "Back out second local frog");
        rig.WaitFor(() => rig.Ready, "Party backout did not complete");
        rig.Steps(160);
        rig.AssertConfirmedStates();
        Check(guest.Lobby.SetSpectating(guest.Lobby.LocalPeer, true), "Become spectator");
        rig.WaitFor(
            () => rig.Ready && guest.Lobby.LobbySession is LobbyNetworkSession { Spectating: true },
            "Spectator transition did not complete"
        );
        rig.Steps(160);
        rig.AssertConfirmedStates();
        Check(guest.Lobby.SetSpectating(guest.Lobby.LocalPeer, false), "Rejoin from spectator");
        rig.WaitFor(() => rig.Ready, "Spectator promotion did not complete");
        rig.Steps(160);
        rig.AssertConfirmedStates();
    }
}
