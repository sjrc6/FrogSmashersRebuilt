using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void SpectatorCanJoinDirectly(int delay)
    {
        using var rig = new Rig
        {
            Delay = delay,
            Jitter = 20,
            Loss = .02,
            DatagramMode = true,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Direct-join fixture did not connect");
        Check(guest.Lobby.SetSpectating(guest.Lobby.LocalPeer, true), "Guest could not request spectating");
        Check(
            guest.Lobby.LocalRequestPending && guest.Lobby.PendingLocalSpectating,
            "Spectate request has no immediate pending state"
        );
        rig.WaitFor(
            () => rig.Ready && guest.Simulation.Membership.Spectator(guest.Lobby.LocalPeer) != null,
            "Guest did not become a spectator"
        );
        Check(!guest.Lobby.LocalRequestPending, "Confirmed spectating request stayed pending");
        int peer = guest.Lobby.LocalPeer;
        long started = rig.Now;
        Check(
            guest.Lobby.SetPlayers([new(2, peer, Color: 5)]),
            "Spectator could not directly join with another device"
        );
        Check(
            guest.Lobby.LocalRequestPending && !guest.Lobby.PendingLocalSpectating,
            "Direct join request has no immediate pending state"
        );
        rig.WaitFor(
            () => rig.Ready && guest.Simulation.Membership.Humans(peer).Any(player => player.Id == 2),
            "Direct spectator join never completed"
        );
        Console.WriteLine($"Slot join at ~{delay * 2} ms RTT: {rig.Now - started} ms including membership checkpoint");
        Check(
            host.Lobby.Roster.Spectator(peer) == null && guest.Lobby.Roster.Spectator(peer) == null,
            "Direct joining left a duplicate spectator"
        );
        rig.Steps(160);
        rig.AssertConfirmedStates();
    }

    private static void RejectedJoinDoesNotPauseLobby()
    {
        using var rig = new Rig { Delay = 120 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Rejected-join fixture did not connect");
        Check(guest.Lobby.SetSpectating(guest.Lobby.LocalPeer, true), "Guest could not spectate");
        rig.WaitFor(
            () => rig.Ready && guest.Simulation.Membership.Spectator(guest.Lobby.LocalPeer) != null,
            "Guest spectate did not complete"
        );
        int peer = guest.Lobby.LocalPeer;
        int checkpoints = rig.CheckpointMessages;
        ulong session = host.Lobby.SessionId;
        Check(guest.Lobby.SetPlayers([new(1, peer)]), "Client could not request its apparently open slot");
        Check(host.Lobby.ApplySlotType(SlotType.Closed), "Host could not close the available slots");
        rig.WaitFor(() => !guest.Lobby.LocalRequestPending, "Rejected join request was never acknowledged");
        Check(
            guest.Lobby.PendingLocalSpectating && guest.Lobby.Roster.Spectator(peer) != null,
            "Rejected join did not preserve spectator state"
        );
        Check(
            rig.CheckpointMessages == checkpoints && host.Lobby.SessionId == session,
            "Rejecting a slot request unnecessarily paused and rebuilt the session"
        );
    }
}
