using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void CpuAppearanceEdits()
    {
        using var rig = new Rig
        {
            Delay = 150,
            Jitter = 30,
            Loss = .03,
            DatagramMode = true,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Appearance fixture did not connect");
        Check(host.Lobby.EditSlot(7, SlotType.Cpu), "CPU creation for appearance editing");
        rig.WaitFor(() => !host.Lobby.IsSlotEditPending(7), "CPU creation did not confirm");
        var original = host.Simulation.Membership.Rooms[7]!;
        var session = host.Lobby.LobbySession;
        int checkpoints = rig.CheckpointMessages;
        Check(!guest.Lobby.ChangeCpu(7, false, 1), "Guests cannot edit CPU colors");
        Check(
            host.Lobby.ChangeCpu(7, false, 1) && host.Lobby.ChangeCpu(7, true, 1),
            "Host can coalesce color and team edits"
        );
        rig.WaitFor(() => host.Lobby.HostCommand.Revision > 0, "Appearance command did not enter the input stream");
        Check(host.Lobby.ChangeCpu(7, true, 1), "A later team edit composes with an in-flight command");
        rig.WaitFor(() => !host.Lobby.IsSlotEditPending(7), "Appearance edits did not confirm");
        rig.Steps(150);
        foreach (var node in rig.Nodes)
        {
            var cpu = node.Simulation.Membership.Rooms[7]!;
            Check(
                cpu.Color != original.Color && cpu.Team == (original.Team + 2) % 8 && cpu.Id == original.Id,
                "Every peer receives the new CPU appearance with stable identity"
            );
            Check(node.Simulation.World.Players[7].ColorIndex == cpu.Color, "CPU body reflects the selected color");
        }
        Check(
            ReferenceEquals(session, host.Lobby.LobbySession) && checkpoints == rig.CheckpointMessages,
            "CPU appearance uses existing rollback rather than restarting the lobby"
        );
        rig.AssertConfirmedStates();
    }

    private static void CpuCommandsDoNotRestartRollback(int delay)
    {
        using var rig = new Rig
        {
            Delay = delay,
            Jitter = 40,
            Loss = .04,
            DatagramMode = true,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "CPU command fixture did not connect");
        rig.Steps(160);
        var sessions = rig.Nodes.Select(node => node.Lobby.LobbySession).ToArray();
        var handles = host.Simulation.InputSources.ToArray();
        int checkpoints = rig.CheckpointMessages;
        foreach (var type in new[] { SlotType.Cpu, SlotType.Local, SlotType.Cpu, SlotType.Closed })
        {
            long before = host.Simulation.World.TickNumber;
            Check(host.Lobby.EditSlot(7, type), "CPU edit rejected");
            rig.WaitFor(() => !host.Lobby.IsSlotEditPending(7), "CPU edit did not confirm", 4800);
            rig.WaitFor(
                () =>
                    rig.Nodes.All(node => (node.Simulation.Membership.Rooms[7]?.Cpu == true) == (type == SlotType.Cpu)),
                "CPU edit did not reach every simulation"
            );
            Check(host.Simulation.World.TickNumber > before, "CPU edits stopped simulation");
            Check(rig.Nodes.All(node => !node.Lobby.Transitioning), "CPU edit entered a checkpoint transition");
        }
        for (int i = 0; i < sessions.Length; i++)
            Check(
                ReferenceEquals(sessions[i], rig.Nodes[i].Lobby.LobbySession),
                "CPU edit replaced a rollback session"
            );
        Check(rig.CheckpointMessages == checkpoints, "CPU edit transferred a checkpoint");
        Check(handles.SequenceEqual(host.Simulation.InputSources), "CPU edit changed human handles");
        rig.Steps(200);
        rig.AssertConfirmedStates();
    }

    private static void CpuCommandsSurviveDeparture(bool submitted)
    {
        using var rig = new Rig { Delay = 80, Jitter = 20 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        host.Lobby.SetRollbackSettings(new() { Delay = RollbackPreferences.MaximumDelayFrames, MaxExtraDelay = 0 });
        var leaving = rig.Add("leaving", [new(0, Spawned: true)]);
        var staying = rig.Add("staying", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Departure fixture did not connect");
        Check(host.Lobby.EditSlot(7, SlotType.Cpu), "Departure CPU request failed");
        if (submitted)
        {
            rig.WaitFor(() => host.Lobby.HostCommand.Revision > 0, "CPU command was not submitted");
            rig.Steps(3);
        }
        ulong session = host.Lobby.SessionId;
        leaving.Active = false;
        leaving.Lobby.Dispose();
        rig.WaitFor(
            () =>
                rig.Ready && !host.Lobby.PeerIds.Contains(leaving.Lobby.LocalPeer) && !host.Lobby.IsSlotEditPending(7),
            "Departure lost an outstanding CPU edit",
            4800
        );
        Check(host.Simulation.Membership.Rooms[7] is { Cpu: true }, "CPU disappeared across departure checkpoint");
        Check(
            host.Simulation.Membership.Humans(staying.Lobby.LocalPeer).Length == 1,
            "CPU command changed the remaining human"
        );
        rig.Steps(240);
        rig.AssertConfirmedStates();
    }

    private static void SpectatingHostEditsCpusAndStartsMatch()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectating CPU fixture did not connect");
        Check(host.Lobby.SetSpectating(0, true), "Host could not spectate");
        rig.WaitFor(
            () => rig.Ready && host.Simulation.Membership.Spectator(0) != null,
            "Host did not begin spectating"
        );
        ulong session = host.Lobby.SessionId;
        Check(
            host.Lobby.EditSlot(0, SlotType.Cpu) && host.Lobby.EditSlot(7, SlotType.Cpu),
            "Spectating host could not add CPUs"
        );
        rig.WaitFor(
            () => !host.Lobby.IsSlotEditPending(0) && !host.Lobby.IsSlotEditPending(7),
            "Spectating host CPU edits did not confirm"
        );
        Check(
            host.Lobby.SessionId == session && host.Simulation.InputSources.Count == 2,
            "CPU edit changed the spectating host session layout"
        );
        Check(host.Lobby.StartMatch(_ => "{}"), "Spectating host could not start a CPU match");
        rig.WaitFor(() => host.Lobby.Ready && guest.Lobby.Ready, "CPU match checkpoint failed");
        Check(
            host.Lobby.InputPlayerSlots.SequenceEqual(new[] { -1, 2 }),
            "CPU frogs were assigned match input streams"
        );
        Check(
            host.Lobby.PeerSlots[0].SequenceEqual(new[] { 0 }) && host.Lobby.PeerSlots[1].SequenceEqual(new[] { 1 }),
            "Spectating host did not retain its command stream"
        );
        Check(host.Lobby.PlayerColors.Length == 3, "CPU bodies disappeared from match configuration");
    }
}
