using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void EmptyParties()
    {
        using var rig = new Rig
        {
            Delay = 90,
            Jitter = 15,
            Loss = .04,
        };
        var host = rig.Add("host", []);
        var guest = rig.Add("guest", []);
        rig.WaitFor(() => rig.Ready, "Empty guest did not receive the lobby stream");
        Check(
            host.Lobby.Roster.Count == 0 && guest.Lobby.LobbySession!.LocalSlots.Length == 0,
            "Connected guests occupy no frog or input-device slots"
        );
        ulong session = host.Lobby.SessionId;
        int peer = guest.Lobby.LocalPeer;
        rig.Steps(150);
        Check(guest.Simulation.World.TickNumber > 100, "An unseated guest sees the live beach ball");
        Check(guest.Lobby.SetPlayers([new(5, Spawned: true)]), "An empty guest can choose a controller explicitly");
        rig.WaitFor(() => rig.Ready, "First explicit local join did not synchronize");
        Check(
            guest.Simulation.Membership.Humans(peer).Single().Id == 5,
            "The chosen controller identity survives admission"
        );
        Check(guest.Lobby.SetPlayers([new(5, Spawned: true), new(1, Spawned: true)]), "A second device can join later");
        rig.WaitFor(() => rig.Ready, "Second local device did not synchronize");
        Check(guest.Lobby.SetPlayers([]), "The last local seat can back out without disconnecting");
        rig.WaitFor(
            () => rig.Ready && guest.Lobby.LobbySession is LobbyNetworkSession { Spectating: true },
            "Last-seat removal did not return to the observer stream"
        );
        Check(
            guest.Lobby.Connected && guest.Lobby.LocalPeer == peer && host.Lobby.SessionId == session,
            "Back-out preserves connection identity and the lobby session"
        );
        Check(
            guest.Lobby.SetPlayers([new(2, Spawned: true), new(3, Spawned: true)]),
            "An unseated guest can bring a new party"
        );
        rig.WaitFor(() => rig.Ready, "Rejoining with new devices failed");
        Check(host.Lobby.StartMatch(_ => "{}"), "An unseated host can start a match for other players");
        rig.WaitFor(() => guest.Lobby.Ready && host.Lobby.Ready, "The unseated host could not complete match start");
        Check(
            host.Lobby.PeerSlots[0].Length == 1 && host.Lobby.InputPlayerSlots[0] == -1,
            "The host owns only its command stream when unseated"
        );
        int generation = host.Lobby.Generation;
        Check(host.Lobby.ReturnToLobby(), "Unseated host can return to lobby");
        rig.WaitFor(() => guest.Lobby.Generation > generation, "Return state did not reach the unseated host's guest");
        foreach (var node in rig.Nodes)
        {
            node.Simulation = new LobbySimulation(
                new World(TestFixtures.Map(), new GameRules(lobby: true, playerCount: 8), 13),
                node.Lobby.Roster
            );
            node.Lobby.AttachSimulation(node.Simulation);
        }
        rig.WaitFor(() => rig.Ready, "Unseated host return failed");
        Check(host.Lobby.RemovePlayer(peer, 2) && host.Lobby.RemovePlayer(peer, 3), "Host can remove both guest seats");
        rig.WaitFor(
            () => rig.Ready && guest.Lobby.LobbySession is LobbyNetworkSession { Spectating: true },
            "Host removal failed to preserve the guest observer"
        );
        Check(
            guest.Lobby.Error == null && guest.Lobby.Connected,
            "Host removing the final seat is separate from kicking a connection"
        );
    }

    private static void ObserverCapacity()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        for (int i = 0; i < LobbyRoster.MaxSpectators; i++)
        {
            rig.Add("observer" + i, []);
            rig.WaitFor(() => rig.Ready, "Unseated observer admission failed");
        }
        var excess = rig.Add("excess", []);
        excess.AllowError = true;
        rig.WaitFor(() => excess.Lobby.Error != null, "Observer capacity was not enforced");
        excess.Active = false;
        Check(
            host.Lobby.PeerIds.Count == 5 && host.Lobby.Roster.Count == 1,
            "Observers consume connection capacity without reserving rooms"
        );
        var player = rig.Add("player", [new(7, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Observer limit incorrectly blocked a playing party");
        Check(!player.Lobby.SetPlayers([]), "Backing out cannot exceed the observer limit");
        var first = rig.Nodes[1];
        Check(first.Lobby.SetPlayers([new(4, Spawned: true)]), "An observer can release observer capacity by joining");
        rig.WaitFor(() => rig.Ready, "Observer-to-player promotion failed");
        Check(player.Lobby.SetPlayers([]), "Released observer capacity permits final-seat removal");
        rig.WaitFor(
            () => rig.Ready && player.Lobby.LobbySession is LobbyNetworkSession { Spectating: true },
            "Back-out failed after freeing capacity"
        );
    }
}
