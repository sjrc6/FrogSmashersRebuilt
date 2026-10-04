using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    private static void SlotPoliciesDoNotRestartRollback(int delay)
    {
        using var rig = new Rig
        {
            Delay = delay,
            Jitter = 40,
            Loss = .04,
            DatagramMode = true,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "High-latency policy fixture did not connect");
        rig.Steps(240);
        var sessions = rig.Nodes.Select(node => node.Lobby.LobbySession).ToArray();
        long tick = host.Simulation.World.TickNumber;
        int checkpointMessages = rig.CheckpointMessages;
        foreach (
            var type in new[] { SlotType.Private, SlotType.Local, SlotType.Friend, SlotType.Closed, SlotType.Open }
        )
        {
            Check(host.Lobby.EditSlot(7, type), "Host could not edit slot policy");
            Check(
                host.Lobby.GetSlotType(7) == type && host.Lobby.Roster.Slots[7].Type == type,
                "Policy edits must be immediately visible and authoritative on the host"
            );
            Check(!host.Lobby.Transitioning && !host.Lobby.IsSlotEditPending(7), "Policy edit paused the lobby");
            rig.Steps(6);
        }
        Check(host.Lobby.EditSlot(7, SlotType.Closed), "Final close failed");
        Check(host.Lobby.Roster.Capacity == 7, "Advertised capacity was not updated immediately");
        rig.WaitFor(() => guest.Lobby.Roster.Slots[7].Type == SlotType.Closed, "Latest policy did not reach guest");
        rig.Steps(240);
        for (int i = 0; i < rig.Nodes.Count; i++)
            Check(
                ReferenceEquals(sessions[i], rig.Nodes[i].Lobby.LobbySession),
                "Policy edit replaced a rollback session"
            );
        Check(host.Simulation.World.TickNumber > tick, "Policy edits stopped gameplay");
        Check(
            rig.CheckpointMessages == checkpointMessages,
            "Policy edit sent checkpoint coordination or snapshot data"
        );
        rig.AssertConfirmedStates();
        Check(!guest.Lobby.EditSlot(7, SlotType.Open), "Guest could alter host admission policy");
    }

    private static void SlotEditsKeepTheLatestChoice()
    {
        using var rig = new Rig { Delay = 250, Jitter = 20 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Queued-edit fixture did not connect");
        ulong original = host.Lobby.SessionId;
        Check(host.Lobby.EditSlot(7, SlotType.Cpu), "CPU request failed");
        Check(
            host.Lobby.GetSlotType(7) == SlotType.Cpu && host.Lobby.IsSlotEditPending(7),
            "CPU selection was not previewed immediately"
        );
        rig.Steps(6);
        Check(host.Lobby.EditSlot(7, SlotType.Private), "Cycling past CPU failed");
        rig.Steps(60);
        Check(
            host.Lobby.SessionId == original && !host.Lobby.IsSlotEditPending(7),
            "Briefly cycling past CPU started a checkpoint"
        );

        Check(host.Lobby.EditSlot(7, SlotType.Cpu), "Second CPU request failed");
        rig.WaitFor(() => host.Lobby.HostCommand.Revision > 0, "CPU command was not submitted");
        Check(host.Lobby.EditSlot(7, SlotType.Closed), "Edit during CPU command was rejected");
        Check(host.Lobby.EditSlot(7, SlotType.Cpu), "Returning to the in-flight type failed");
        Check(host.Lobby.EditSlot(7, SlotType.Friend), "Latest type was rejected");
        Check(host.Lobby.GetSlotType(7) == SlotType.Friend, "Editor did not keep the latest requested type");
        Check(
            host.Lobby.EditSlot(6, SlotType.Cpu) && host.Lobby.EditSlot(6, SlotType.Local),
            "Independent queued edits failed"
        );
        Check(host.Lobby.EditSlot(5, SlotType.Closed), "Policy change during CPU command failed");
        Check(!host.Lobby.StartMatch(_ => "{}"), "Match started with unapplied slot edits");
        rig.WaitFor(
            () =>
                rig.Ready
                && !host.Lobby.IsSlotEditPending(7)
                && host.Simulation.Membership.Rooms[7] == null
                && host.Lobby.Roster.Slots[7].Type == SlotType.Friend,
            "Queued CPU removal did not finish",
            4800
        );
        rig.WaitFor(
            () =>
                rig.Nodes.All(node =>
                    node.Lobby.Roster.Slots[7].Type == SlotType.Friend
                    && node.Lobby.Roster.Slots[6].Type == SlotType.Local
                    && node.Lobby.Roster.Slots[5].Type == SlotType.Closed
                ),
            "CPU confirmation overwrote newer access rules"
        );
        Check(rig.Nodes.All(node => node.Simulation.Membership.Count == 2), "Canceled CPU requests left extra frogs");
        rig.Steps(160);
        rig.AssertConfirmedStates();
    }

    private static void ApplyAllDuringCpuTransition()
    {
        using var rig = new Rig
        {
            Delay = 160,
            Jitter = 40,
            Loss = .04,
            DatagramMode = true,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Apply-all fixture did not connect");
        Check(host.Lobby.EditSlot(7, SlotType.Cpu), "CPU request failed");
        rig.WaitFor(() => host.Lobby.HostCommand.Revision > 0, "CPU command was not submitted");
        Check(host.Lobby.ApplySlotType(SlotType.Friend), "Apply-all was rejected during a CPU command");
        Check(host.Lobby.EditSlot(4, SlotType.Closed), "Individual override after apply-all failed");
        Check(
            host.Lobby.GetSlotType(7) == SlotType.Friend && host.Lobby.GetSlotType(4) == SlotType.Closed,
            "Apply-all did not preserve the latest individual choices"
        );
        rig.WaitFor(
            () => rig.Ready && !host.Lobby.IsSlotEditPending(7) && host.Simulation.Membership.Rooms[7] == null,
            "Apply-all queue did not drain",
            4800
        );
        rig.WaitFor(
            () =>
                rig.Nodes.All(node =>
                    node.Lobby.Roster.Slots[7].Type == SlotType.Friend
                    && node.Lobby.Roster.Slots[4].Type == SlotType.Closed
                ),
            "Final apply-all metadata did not arrive"
        );
        Check(
            host.Lobby.Roster.Count == 2 && host.Lobby.Roster.Capacity == 7,
            "Apply-all affected occupied humans or lost capacity"
        );
        var denied = rig.Add("denied", [new(0)]);
        denied.AllowError = true;
        rig.WaitFor(() => denied.Lobby.Error != null, "Admission ignored the latest friend/closed policies");
        Check(!denied.Lobby.Connected && host.Lobby.Roster.Count == 2, "Admission consumed an ineligible slot");
        rig.Steps(120);
        rig.AssertConfirmedStates();
    }

    private static bool IsCheckpointControl(byte[] data)
    {
        if (data.Length < 5 || data[4] != 1)
            return false;
        var control = System.Text.Json.JsonSerializer.Deserialize<MeshLobby.Control>(data.AsSpan(5));
        return control?.Kind
            is MeshLobby.ControlKind.Pause
                or MeshLobby.ControlKind.Confirm
                or MeshLobby.ControlKind.Checkpoint
                or MeshLobby.ControlKind.Chunk
                or MeshLobby.ControlKind.Commit;
    }

    private static void BatchedCpuEditsPreserveIdentity()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        Check(host.Lobby.EditSlot(1, SlotType.Cpu), "Initial CPU request failed");
        rig.WaitFor(
            () => rig.Ready && !host.Lobby.IsSlotEditPending(1) && host.Simulation.Membership.Rooms[1] is { Cpu: true },
            "Initial CPU did not spawn"
        );
        int oldId = host.Simulation.Membership.Rooms[1]!.Id;
        Check(
            host.Lobby.EditSlot(1, SlotType.Local) && host.Lobby.EditSlot(7, SlotType.Cpu),
            "Batched CPU edit failed"
        );
        rig.WaitFor(
            () =>
                rig.Ready
                && !host.Lobby.IsSlotEditPending(1)
                && !host.Lobby.IsSlotEditPending(7)
                && host.Simulation.Membership.Rooms[1] == null
                && host.Simulation.Membership.Rooms[7] is { Cpu: true },
            "Batched CPU edits did not finish"
        );
        Check(
            host.Simulation.Membership.Rooms[7]!.Id != oldId,
            "A new CPU inherited the removed bot's identity, body or pending inputs"
        );
    }
}
