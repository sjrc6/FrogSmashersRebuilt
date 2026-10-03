using System.Text;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static partial class LobbyTests
{
    public static void Run(Action<bool, string> check)
    {
        JoiningUsesConfirmedCheckpoints();
        CommandsStayInTheInputStream();
        PendingCommandsSurviveMembershipChanges();
        PersonalTimingSurvivesCheckpoints();
        GrowingDelayDoesNotRepeatLobbyCommands();
        FallingDelayRetainsButtonEdges();
        MultiplePartyEditsBeforePolling();
        MeshFailureCancelsAdmission();
        LeavingKeepsPeerIdentityStable();
        MatchStartAndReturn();
        HostCanSpectate();
        SpectatorTransitionAtLatency();
        InvalidClientsAndPackets();
        LateSimulationAndLatestSettings();
        DisconnectDuringAdmission();
        DelayedSpectatorDoesNotStopPlayers();
        CheckpointsOverLossyDatagrams();
        SpectatorLeavingDoesNotEndMatch();
        LostKickCannotReadmitTheSameClient();
        LeavingWhileEnteringSpectate();
        SlotPoliciesDoNotRestartRollback(250);
        SlotPoliciesDoNotRestartRollback(500);
        SlotEditsKeepTheLatestChoice();
        ApplyAllDuringCpuTransition();
        BatchedCpuEditsPreserveIdentity();
        CpuCommandsDoNotRestartRollback(250);
        CpuCommandsDoNotRestartRollback(500);
        CpuCommandsSurviveDeparture(false);
        CpuCommandsSurviveDeparture(true);
        SpectatingHostEditsCpusAndStartsMatch();
        SpectatorCanJoinDirectly(250);
        SpectatorCanJoinDirectly(500);
        RejectedJoinDoesNotPauseLobby();
        SteadyLobbyUsesCompactControls();
        CompactControlValidation();
    }

    private static void SpectatorTransitionAtLatency()
    {
        using var rig = new Rig { Delay = 100, Jitter = 16 };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectator transition fixture did not synchronize");
        ulong previous = host.Lobby.SessionId;
        Check(guest.Lobby.SetSpectating(guest.Lobby.LocalPeer, true), "Guest can become a spectator at 200 ms RTT");
        rig.WaitFor(() => rig.Ready && host.Lobby.SessionId == previous, "Spectator transition did not finish");
        Check(guest.Lobby.LobbySession!.LocalSlots.Length == 0, "The former player uses the host's spectator stream");
        rig.Steps(120);
        long started = guest.Simulation.World.TickNumber;
        int waits = 0;
        for (int frame = 0; frame < 600; frame++)
        {
            rig.Steps(1);
            waits += guest.Lobby.LobbySession.WaitingForHostInputs ? 1 : 0;
        }
        Check(waits == 0, "A healthy stream after changing to spectator does not repeatedly show input waits");
        Check(guest.Simulation.World.TickNumber >= started + 599, "Spectator transition retains smooth playback");
        rig.AssertConfirmedStates();
    }

    private static void PersonalTimingSurvivesCheckpoints()
    {
        using var rig = new Rig
        {
            DatagramMode = true,
            Delay = 20,
            Jitter = 12,
            Loss = .03,
        };
        var host = rig.Add("host", Enumerable.Range(0, 4).Select(id => new LobbyPlayer(id)).ToArray());
        host.Lobby.SetRollbackSettings(new() { Delay = RollbackPreferences.MaximumDelayFrames, MaxExtraDelay = 0 });
        rig.Input = (node, handle) => node == host && handle == 1 ? new(default, TeamStep: 1) : default;
        rig.Steps(240);
        var guest = rig.Add("guest", Enumerable.Range(0, 3).Select(id => new LobbyPlayer(id, Team: 1)).ToArray());
        guest.Lobby.SetRollbackSettings(
            new()
            {
                Delay = 37,
                Donation = 3,
                MaxExtraDelay = 0,
            }
        );
        rig.WaitFor(() => rig.Ready, "Long input prefixes did not survive admission");
        rig.Steps(240);
        var last = rig.Add("last", [new(0, Team: 2)]);
        last.Lobby.SetRollbackSettings(new() { Delay = 0, MaxExtraDelay = 12 });
        rig.WaitFor(() => rig.Ready, "Eight-player delayed checkpoint failed");
        rig.Steps(240);
        Check(
            host.Simulation.Membership.Humans(0)[0].Team
                == (host.Simulation.World.TickNumber - RollbackPreferences.MaximumDelayFrames) % 8,
            "Long checkpoint prefix dropped or repeated an accepted lobby command"
        );
        foreach (var node in rig.Nodes)
        {
            Check(
                node.Lobby.PeerRollbackSettings[0] == host.Lobby.RollbackSettings
                    && node.Lobby.PeerRollbackSettings[guest.Lobby.LocalPeer] == guest.Lobby.RollbackSettings
                    && node.Lobby.PeerRollbackSettings[last.Lobby.LocalPeer] == last.Lobby.RollbackSettings,
                "Every machine sees the personal settings of every other machine"
            );
            int polls = node.Wire.PollCount;
            node.Lobby.LobbySession!.Poll();
            node.Lobby.LobbySession.TryAdvance(
                node.Lobby.LobbySession.LocalSlots.Select(_ => default(RollbackInput)).ToArray()
            );
            Check(
                node.Wire.PollCount == polls,
                "Rollback polling and advancement cannot pump lobby transport recursively"
            );
            node.Lobby.Poll();
            Check(node.Wire.PollCount == polls + 1, "One outer lobby poll pumps transport exactly once");
        }
        rig.Steps(60);
        rig.AssertConfirmedStates();
        Check(
            rig.Sent.All(packet => packet.Data.Length <= 8192),
            "Long delayed-input checkpoints respect transport bounds"
        );
    }

    private static void GrowingDelayDoesNotRepeatLobbyCommands()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0)]);
        host.Lobby.SetRollbackSettings(new() { Delay = 0, MaxExtraDelay = 0 });
        rig.Input = (_, _) => new(default, TeamStep: 1);
        rig.Steps(1);
        Check(host.Simulation.Membership.Rooms[0]!.Team == 1, "Fixture applies one lobby command");
        host.Lobby.SetRollbackSettings(new() { Delay = 12, MaxExtraDelay = 0 });
        rig.Input = null;
        rig.Steps(30);
        Check(
            host.Simulation.Membership.Rooms[0]!.Team == 1,
            "Increasing response delay pads held controls without repeating one-shot lobby commands"
        );
    }

    private static void FallingDelayRetainsButtonEdges()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0)]);
        host.Lobby.SetRollbackSettings(new() { Delay = 12, MaxExtraDelay = 0 });
        rig.Steps(30);
        host.Lobby.SetRollbackSettings(new() { Delay = 0, MaxExtraDelay = 0 });
        var pending = new RollbackInput(default, TeamStep: 1);
        rig.Input = (_, _) => pending;
        int accepted = 0;
        for (int step = 0; step < 30; step++)
        {
            rig.Steps(1);
            if (host.Lobby.LobbySession!.LocalInputSubmitted)
            {
                accepted += pending.TeamStep;
                pending = default;
            }
        }
        Check(
            accepted == 1 && host.Simulation.Membership.Rooms[0]!.Team == 1,
            "Reducing delay retains a pending button edge until a new sample is accepted exactly once"
        );
    }

    private static void MultiplePartyEditsBeforePolling()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0)]);
        var guest = rig.Add("guest", [new(0)]);
        rig.WaitFor(() => rig.Ready, "Party composition fixture did not synchronize");
        foreach (var node in new[] { host, guest })
        {
            ulong previousSession = host.Lobby.SessionId;
            for (int id = 1; id <= 2; id++)
                Check(
                    node.Lobby.SetPlayers([.. node.Lobby.PendingLocalPlayers, new(id, Color: id)]),
                    "A second same-render join request was rejected"
                );
            Check(
                node.Lobby.PendingLocalPlayers.Select(player => player.Id).SequenceEqual(new[] { 0, 1, 2 }),
                "A same-render join replaced an earlier pending device"
            );
            Check(
                node.Simulation.Membership.Humans(node.Lobby.LocalPeer).Length == 1,
                "Pending party edits mutated the committed simulation before its checkpoint"
            );
            rig.WaitFor(
                () => rig.Ready && host.Lobby.SessionId == previousSession,
                "Same-render joins did not finish their checkpoint"
            );
            Check(
                rig.Nodes.All(other =>
                    other
                        .Simulation.Membership.Humans(node.Lobby.LocalPeer)
                        .Select(player => player.Id)
                        .Order()
                        .SequenceEqual(new[] { 0, 1, 2 })
                ),
                "A pending local device was lost during admission"
            );
        }
        ulong joinedSession = host.Lobby.SessionId;
        for (int id = 1; id <= 2; id++)
            Check(
                guest.Lobby.SetPlayers(guest.Lobby.PendingLocalPlayers.Where(player => player.Id != id).ToArray()),
                "A second same-render back-out request was rejected"
            );
        Check(
            guest.Lobby.PendingLocalPlayers.Select(player => player.Id).SequenceEqual(new[] { 0 }),
            "A same-render back-out restored an earlier removed device"
        );
        rig.WaitFor(
            () => rig.Ready && host.Lobby.SessionId == joinedSession,
            "Same-render back-outs did not finish their checkpoint"
        );
        Check(
            rig.Nodes.All(node => node.Simulation.Membership.Humans(guest.Lobby.LocalPeer).Length == 1),
            "The last party request was not committed on every machine"
        );
        rig.Steps(60);
        rig.AssertConfirmedStates();
    }

    private static void JoiningUsesConfirmedCheckpoints()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        rig.Steps(80);
        long beforeJoin = host.Simulation.World.TickNumber;
        ulong firstSession = host.Lobby.SessionId;
        var guest = rig.Add("guest", [new(0), new(1, Color: 1, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "First party admission did not complete");
        Check(
            host.Lobby.Roster.Count == 3 && guest.Lobby.Roster.Count == 3,
            "Joining local party did not receive all rooms"
        );
        Check(host.Simulation.World.TickNumber >= beforeJoin, "Admission reset the incumbent world clock");
        Check(
            host.Lobby.SessionId == firstSession && host.Lobby.SessionId == guest.Lobby.SessionId,
            "Admission replaced the running rollback session"
        );
        rig.Steps(100);
        rig.AssertConfirmedStates();
    }

    private static void CommandsStayInTheInputStream()
    {
        using var rig = new Rig
        {
            Delay = 45,
            Jitter = 35,
            Loss = 0.03,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0), new(1, Color: 1, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Two-peer lobby did not synchronize");
        var third = rig.Add("third", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Third peer could not join a running lobby");
        var fourth = rig.Add("fourth", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Fourth peer could not join a running lobby");
        rig.Steps(80);
        ulong session = host.Lobby.SessionId;
        int generation = host.Lobby.Generation;
        int room = Enumerable
            .Range(0, 8)
            .Single(index =>
                guest.Simulation.Membership.Rooms[index] is { } player
                && player.Peer == guest.Lobby.LocalPeer
                && player.Id == 0
            );
        int color = guest.Simulation.Membership.Rooms[room]!.Color;
        long colorTick = guest.Simulation.World.TickNumber + 4;
        rig.Input = (node, handle) =>
            node == guest
            && node.Simulation.InputSources[handle].Id == 0
            && node.Simulation.World.TickNumber == colorTick
                ? new(default, ColorStep: 1, TeamStep: 1)
                : default;
        rig.Steps(100);
        Check(
            rig.Nodes.All(node =>
                node.Simulation.Membership.Rooms[room] is { Team: 1 } player && player.Color != color
            ),
            "Color and team commands did not converge through rollback"
        );
        long spawnTick = guest.Simulation.World.TickNumber + 4;
        rig.Input = (node, handle) =>
            node == guest
            && node.Simulation.InputSources[handle].Id == 0
            && node.Simulation.World.TickNumber == spawnTick
                ? new(default, (byte)LobbyInputActions.Spawn)
                : default;
        rig.Steps(100);
        Check(
            rig.Nodes.All(node => node.Simulation.Membership.Rooms[room] is { Spawned: true }),
            "Spawn action did not converge through rollback"
        );
        Check(
            host.Lobby.SessionId == session && host.Lobby.Generation == generation && !host.Lobby.Transitioning,
            "Routine color/spawn actions restarted the lobby session"
        );
        Check(
            rig.Sent.Any(packet => packet.Source == "guest" && packet.Destination == "third" && IsInput(packet)),
            "Guest inputs never traveled directly to another guest"
        );
        Check(
            rig.Sent.Where(IsInput)
                .All(packet =>
                    packet.Data[13] == rig.Nodes.Single(node => node.Wire.LocalAddress == packet.Source).Lobby.LocalPeer
                ),
            "A machine forwarded another active player's input stream"
        );
        rig.AssertConfirmedStates();
        rig.Input = null;
        Check(host.Lobby.EditSlot(7, SlotType.Closed), "Host could not close an unused room");
        rig.WaitFor(() => rig.Nodes.All(node => node.Lobby.Roster.Capacity == 7), "Capacity metadata did not arrive");
        Check(
            rig.Nodes.All(node => node.Lobby.Roster.Capacity == 7 && node.Simulation.Membership.Rooms[7] == null),
            "Slot capacity did not propagate as metadata"
        );
        Check(host.Lobby.SessionId == session, "Capacity change restarted rollback");
        Check(host.Lobby.SetPlayers([.. host.Lobby.Roster.Players(0), new(1)]), "Host could not add a human");
        rig.WaitFor(() => rig.Ready && host.Lobby.SessionId == session, "Human admission checkpoint did not complete");
        rig.Steps(80);
        rig.AssertConfirmedStates();
        var old = rig.Sent.Last(packet =>
            packet.Source == "guest"
            && packet.Destination == "host"
            && IsInput(packet)
            && BitConverter.ToUInt64(packet.Data, 5) == session
        );
        int rejected = host.Lobby.LobbySession!.RejectedPackets;
        host.Wire.Incoming.Enqueue(new("guest", old.Data));
        host.Lobby.Poll();
        Check(
            host.Lobby.LobbySession.RejectedPackets == rejected,
            "An old generation datagram reached the new rollback controller"
        );
        var forged = rig
            .Sent.Last(packet => packet.Source == "third" && packet.Destination == "host" && IsInput(packet))
            .Data.ToArray();
        host.Wire.Incoming.Enqueue(new("guest", forged));
        host.Lobby.Poll();
        Check(
            host.Lobby.LobbySession.RejectedPackets == rejected,
            "A guest spoofed another peer's mesh input identity"
        );
    }

    private static void PendingCommandsSurviveMembershipChanges()
    {
        using var rig = new Rig
        {
            Delay = 35,
            Jitter = 25,
            Loss = 0.02,
        };
        var host = rig.Add("host", [new(0)]);
        rig.Input = (node, handle) => node == host ? new(default, TeamStep: 1) : default;
        rig.Steps(80);
        Check(
            host.Simulation.Membership.Rooms[0]!.Team == (host.Simulation.World.TickNumber - 2) % 8,
            "Fixture did not establish the two-frame input delay"
        );
        rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Pending-command fixture admission failed");
        rig.Steps(120);
        Check(
            host.Simulation.Membership.Rooms[0]!.Team == (host.Simulation.World.TickNumber - 2) % 8,
            "Checkpoint dropped or repeated a queued one-shot team command"
        );
        rig.Add("other", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Second pending-command fixture admission failed");
        rig.Steps(120);
        Check(
            host.Simulation.Membership.Rooms[0]!.Team == (host.Simulation.World.TickNumber - 2) % 8,
            "Second checkpoint lost the original input-delay continuity"
        );
        rig.AssertConfirmedStates();
    }

    private static void MeshFailureCancelsAdmission()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var existing = rig.Add("existing", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Initial mesh did not synchronize");
        rig.Steps(80);
        ulong generation = host.Lobby.SessionId;
        rig.Blocked.Add(("existing", "blocked"));
        rig.Blocked.Add(("blocked", "existing"));
        var newcomer = rig.Add("blocked", [new(0, Spawned: true)]);
        newcomer.AllowError = true;
        rig.WaitFor(() => newcomer.Lobby.Connected, "New admission was not introduced");
        rig.WaitFor(
            () => host.Lobby.SimulationReady && !host.Lobby.PeerIds.Contains(newcomer.Lobby.LocalPeer),
            "Failed mesh admission did not resume the old lobby",
            2400
        );
        long resumedTick = host.Simulation.World.TickNumber;
        newcomer.Active = false;
        rig.Steps(150);
        Check(
            host.Lobby.SessionId == generation && existing.Lobby.SessionId == generation,
            "Cancelled admission replaced the existing generation"
        );
        Check(
            host.Simulation.World.TickNumber > resumedTick + 80,
            "Existing lobby did not recover promptly after cancellation"
        );
        Check(
            host.Lobby.Roster.Count == 2 && existing.Lobby.Roster.Count == 2,
            "Cancelled admission retained an unconnected player"
        );
        rig.AssertConfirmedStates();
    }

    private static void LeavingKeepsPeerIdentityStable()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var first = rig.Add("first", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "First guest failed to synchronize");
        var second = rig.Add("second", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Second guest failed to synchronize");
        rig.Steps(80);
        int survivingId = second.Lobby.LocalPeer;
        ulong oldSession = host.Lobby.SessionId;
        first.Lobby.Dispose();
        first.Active = false;
        rig.WaitFor(
            () => rig.Ready && !host.Lobby.PeerIds.Contains(first.Lobby.LocalPeer),
            "Departure did not converge"
        );
        Check(
            second.Lobby.LocalPeer == survivingId && host.Lobby.PeerIds.SequenceEqual(new[] { 0, survivingId }),
            "Leaving a peer renumbered the surviving connection"
        );
        rig.Steps(100);
        rig.AssertConfirmedStates();
        Check(host.Lobby.EditSlot(7, SlotType.Cpu), "Host could not create a CPU");
        oldSession = host.Lobby.SessionId;
        rig.WaitFor(() => !host.Lobby.IsSlotEditPending(7), "CPU command failed");
        Check(host.Lobby.SessionId == oldSession, "CPU command changed the session");
        rig.Steps(80);
        Check(
            host.Simulation.Membership.Rooms[7] is { Cpu: true }
                && host.Simulation.World.Players[7].PreviousInput == default,
            "New lobby CPU was missing or attacked without provocation"
        );
        rig.AssertConfirmedStates();
    }

    private static void MatchStartAndReturn()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Match fixture did not synchronize");
        rig.Steps(80);
        Check(
            !guest.Lobby.StartMatch("{}") && host.Lobby.StartMatch("{\"seed\":9}"),
            "Match start authority was not enforced"
        );
        rig.WaitFor(() => host.Lobby.Ready && guest.Lobby.Ready, "Match start checkpoint did not complete");
        Check(
            host.Lobby.PeerSlots.Length == 2
                && host.Lobby.PeerSlots[0].SequenceEqual(new[] { 0, 1 })
                && host.Lobby.PeerSlots[1].SequenceEqual(new[] { 2 })
                && host.Lobby.InputPlayerSlots.SequenceEqual(new[] { -1, 0, 1 }),
            "Frozen match input ownership was incorrect"
        );
        Check(
            host.Lobby.MatchSettingsJson == guest.Lobby.MatchSettingsJson
                && host.Lobby.PlayerColors.SequenceEqual(guest.Lobby.PlayerColors),
            "Match configuration differed across peers"
        );
        Check(!host.Lobby.EditSlot(7, SlotType.Closed), "Match accepted a roster edit");
        var late = rig.Add("late", [new(0)]);
        late.AllowError = true;
        rig.WaitFor(() => late.Lobby.Error != null, "Match did not close joining");
        Check(!late.Lobby.Connected && host.Lobby.Roster.Count == 2, "Late join changed the match roster");
        late.Active = false;
        int previousGeneration = host.Lobby.Generation;
        Check(
            !guest.Lobby.ReturnToLobby() && host.Lobby.ReturnToLobby(),
            "Only the host should return the whole match to lobby"
        );
        rig.WaitFor(() => guest.Lobby.Generation > previousGeneration, "Return-to-lobby state was not broadcast");
        foreach (var node in rig.Nodes.Where(node => node.Active))
        {
            node.Simulation = new LobbySimulation(
                new World(TestFixtures.Map(), new GameRules { Lobby = true, PlayerCount = 8 }, 13),
                node.Lobby.Roster,
                71
            );
            node.Lobby.AttachSimulation(node.Simulation);
        }
        rig.WaitFor(() => rig.Ready, "Returned lobby did not create a new rollback session");
        rig.Steps(80);
        rig.AssertConfirmedStates();
    }

    private static void HostCanSpectate()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var guest = rig.Add("guest", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectator fixture did not synchronize");
        var other = rig.Add("other", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Third spectator fixture peer did not synchronize");
        rig.Steps(80);
        ulong session = host.Lobby.SessionId;
        Check(host.Lobby.SetSpectating(0, true), "Host could not select spectating");
        rig.WaitFor(() => rig.Ready && host.Lobby.SessionId == session, "Spectating host checkpoint failed");
        Check(
            host.Lobby.LobbySession!.LocalSlots.SequenceEqual(new[] { 0 }) && host.Lobby.Roster.Spectator(0) != null,
            "Spectating host must retain only the host command stream"
        );
        rig.Steps(180);
        rig.AssertConfirmedStates();
        session = host.Lobby.SessionId;
        Check(other.Lobby.SetSpectating(other.Lobby.LocalPeer, true), "Guest could not spectate");
        rig.WaitFor(() => rig.Ready && host.Lobby.SessionId == session, "Guest spectator checkpoint failed");
        rig.Steps(80);
        int spectatorPackets = rig.Sent.Count;
        rig.Steps(180);
        Check(other.Lobby.LobbySession!.LocalSlots.Length == 0, "Spectator retained an active input slot");
        Check(
            rig.Sent.Skip(spectatorPackets)
                .Where(packet =>
                    IsInput(packet)
                    && packet.Destination == "other"
                    && BitConverter.ToUInt64(packet.Data, 5) == host.Lobby.SessionId
                )
                .All(packet => packet.Source == "host"),
            "Spectator stream was sent by a nonhost player"
        );
        Check(
            other.Simulation.World.TickNumber > other.Lobby.LobbySession.StartTick + 60,
            "Host-fed spectator did not advance"
        );
        session = host.Lobby.SessionId;
        Check(
            other.Lobby.SetSpectating(other.Lobby.LocalPeer, false),
            "Spectator could not return to a free player slot"
        );
        rig.WaitFor(() => rig.Ready && host.Lobby.SessionId == session, "Unspectating checkpoint failed");
        rig.Steps(100);
        rig.AssertConfirmedStates();
    }

    private static void InvalidClientsAndPackets()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        foreach (
            string json in new[]
            {
                "null",
                "{",
                "[]",
                "{\"Kind\":null}",
                "{\"Nonce\":null}",
                "{\"Peers\":[null]}",
                "{\"Players\":[null]}",
                "{\"Inputs\":[null]}",
            }
        )
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] packet = new byte[body.Length + 5];
            BitConverter.TryWriteBytes(packet, 0x46534D31u);
            packet[4] = 1;
            body.CopyTo(packet, 5);
            host.Wire.Incoming.Enqueue(new("stranger", packet));
        }
        rig.Steps(4);
        Check(host.Lobby.Error == null && host.Lobby.Roster.Count == 1, "Malformed control altered the host roster");
        var wrong = rig.Add("wrong", [new(0)], "other-content");
        wrong.AllowError = true;
        rig.WaitFor(() => wrong.Lobby.Error != null, "Mismatched content was not rejected");
        Check(
            !wrong.Lobby.Connected && host.Lobby.PeerIds.SequenceEqual(new[] { 0 }),
            "Mismatched build joined the mesh"
        );
    }

    private static void LateSimulationAndLatestSettings()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        rig.Steps(80);
        ulong original = host.Lobby.SessionId;
        var guest = rig.Add("late-simulation", [new(0, Spawned: true)], attach: false);
        rig.Steps(180);
        Check(
            !host.Lobby.Transitioning && guest.Lobby.Connected && guest.Lobby.LobbySession == null,
            "Late simulation fixture did not defer checkpoint installation"
        );
        host.Lobby.SetMatchSettings("{\"latest\":1}");
        long paused = host.Simulation.World.TickNumber;
        rig.Steps(80);
        Check(host.Simulation.World.TickNumber >= paused + 79, "Loading a newcomer paused the host");
        guest.Lobby.AttachSimulation(guest.Simulation);
        rig.WaitFor(() => rig.Ready, "A client attaching after all snapshot chunks could not finish admission");
        Check(
            host.Lobby.SessionId == original
                && host.Lobby.MatchSettingsJson == "{\"latest\":1}"
                && guest.Lobby.MatchSettingsJson == host.Lobby.MatchSettingsJson,
            "Settings edited during admission were lost on checkpoint commit"
        );
        ulong current = host.Lobby.SessionId;
        host.Lobby.SetMatchSettings("{\"latest\":2}");
        rig.Steps(100);
        Check(
            host.Lobby.SessionId == current && guest.Lobby.MatchSettingsJson == "{\"latest\":2}",
            "Settings-only edits restarted rollback or failed to propagate"
        );
        rig.AssertConfirmedStates();
    }

    private static void DisconnectDuringAdmission()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var existing = rig.Add("existing", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Disconnect fixture initial synchronization failed");
        var leaving = rig.Add("leaving", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Disconnect fixture third peer failed");
        rig.Steps(80);
        rig.Blocked.Add(("existing", "newcomer"));
        rig.Blocked.Add(("newcomer", "existing"));
        var newcomer = rig.Add("newcomer", [new(0, Spawned: true)]);
        newcomer.AllowError = true;
        rig.WaitFor(() => newcomer.Lobby.Connected, "Admission fixture did not start");
        leaving.Lobby.Dispose();
        leaving.Active = false;
        rig.WaitFor(() => newcomer.Lobby.Error != null, "Disconnect did not cancel the pending newcomer");
        newcomer.Active = false;
        rig.WaitFor(
            () => rig.Ready,
            "Surviving clients could not drain their old session after another peer left during admission"
        );
        Check(
            host.Lobby.PeerIds.SequenceEqual(new[] { 0, existing.Lobby.LocalPeer }) && host.Lobby.Roster.Count == 2,
            "Cancelled admission/disconnect retained stale roster members"
        );
        rig.Steps(100);
        rig.AssertConfirmedStates();
    }

    private static void DelayedSpectatorDoesNotStopPlayers()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var player = rig.Add("player", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectator delay fixture did not synchronize");
        var observer = rig.Add("observer", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Observer admission failed");
        ulong original = host.Lobby.SessionId;
        Check(observer.Lobby.SetSpectating(observer.Lobby.LocalPeer, true), "Observer could not select spectating");
        rig.WaitFor(() => rig.Ready && host.Lobby.SessionId == original, "Observer transition failed");
        rig.Steps(80);
        original = host.Lobby.SessionId;
        rig.DeliveryDelay["observer"] = 2000;
        Check(
            host.Lobby.SetPlayers([.. host.Lobby.Roster.Players(0), new(1)]),
            "Host could not trigger spectator delay fixture checkpoint"
        );
        rig.WaitFor(
            () =>
                host.Lobby.SimulationReady
                && player.Lobby.SimulationReady
                && host.Lobby.SessionId == original
                && player.Lobby.SessionId == host.Lobby.SessionId
                && host.Lobby.LobbySession?.State == GGCS.SessionState.Running
                && player.Lobby.LobbySession?.State == GGCS.SessionState.Running,
            "Delayed spectator blocked active players' checkpoint"
        );
        Check(observer.Lobby.SessionId == original, "Spectator delay fixture did not delay installation");
        long before = host.Simulation.World.TickNumber;
        rig.Steps(100);
        Check(
            host.Simulation.World.TickNumber > before + 80,
            "Active players waited for spectator checkpoint delivery: " + rig.Describe() + $" (before={before})"
        );
        rig.DeliveryDelay.Clear();
        rig.WaitFor(() => rig.Ready, "Delayed spectator did not install the new session");
        rig.Steps(240);
        Check(
            observer.Simulation.World.TickNumber > observer.Lobby.LobbySession!.StartTick + 100,
            "Delayed spectator did not resume its confirmed stream"
        );
    }

    private static void CheckpointsOverLossyDatagrams()
    {
        using var rig = new Rig
        {
            DatagramMode = true,
            Delay = 40,
            Jitter = 50,
            Loss = 0.12,
        };
        var host = rig.Add("host", [new(0, Spawned: true)]);
        rig.Steps(80);
        rig.Add("first", [new(0, Spawned: true), new(1, Color: 1, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Lossy reliable UDP could not admit a local party");
        rig.Add("second", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Lossy reliable UDP could not establish a full mesh");
        rig.Steps(250);
        rig.AssertConfirmedStates();
        Check(
            rig.Sent.Max(packet => packet.Data.Length) <= 1232,
            "Integrated checkpoint exceeded the UDP datagram budget"
        );
    }

    private static void SpectatorLeavingDoesNotEndMatch()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var player = rig.Add("player", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectator leave fixture did not synchronize");
        var observer = rig.Add("observer", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectator leave fixture admission failed");
        ulong oldSession = host.Lobby.SessionId;
        Check(observer.Lobby.SetSpectating(observer.Lobby.LocalPeer, true), "Observer could not enter spectate");
        rig.WaitFor(
            () => rig.Ready && host.Lobby.SessionId == oldSession,
            "Observer could not finish entering spectate"
        );
        rig.Steps(80);
        Check(host.Lobby.StartMatch("{}"), "Spectator leave fixture could not start a match");
        rig.WaitFor(
            () => rig.Nodes.All(node => node.Lobby.Ready),
            "Spectator leave fixture did not finish match setup"
        );
        ulong matchSession = host.Lobby.SessionId;
        int generation = host.Lobby.Generation;
        observer.Lobby.Dispose();
        observer.Active = false;
        rig.Steps(100);
        Check(
            host.Lobby.Starting && host.Lobby.Ready && player.Lobby.Starting && player.Lobby.Ready,
            "A spectator leaving returned active players to the lobby"
        );
        Check(
            host.Lobby.SessionId == matchSession && host.Lobby.Generation == generation && host.Lobby.Roster.Count == 2,
            "Spectator departure changed the active match generation or player roster"
        );
        Check(
            !host.Lobby.PeerIds.Contains(observer.Lobby.LocalPeer),
            "Departed spectator remained in the connection roster"
        );
    }

    private static void LostKickCannotReadmitTheSameClient()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var kicked = rig.Add("kicked", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Kick fixture did not synchronize");
        rig.Steps(80);
        Check(host.Lobby.SetSpectating(0, true), "Kick fixture could not begin a roster change");
        rig.Steps(1);
        Check(host.Lobby.EditSlot(7, SlotType.Private), "Could not change a policy during a checkpoint");
        rig.Blocked.Add(("host", "kicked"));
        host.Lobby.Kick(kicked.Lobby.LocalPeer, false);
        rig.WaitFor(
            () => host.Lobby.SimulationReady && host.Lobby.PeerIds.Count == 1,
            "Kicking during a roster change did not recover the host"
        );
        long before = host.Simulation.World.TickNumber;
        rig.Steps(240);
        Check(kicked.Lobby.Error == null, "Kick-loss fixture unexpectedly delivered the rejection");
        Check(
            host.Lobby.PeerIds.SequenceEqual(new[] { 0 })
                && host.Lobby.Roster.Count == 0
                && host.Lobby.Roster.Spectator(0) != null
                && !host.Lobby.Transitioning,
            "Periodic hello from a removed client automatically rejoined the lobby"
        );
        Check(
            host.Simulation.World.TickNumber > before + 180,
            "Removed client's retries repeatedly paused the surviving lobby"
        );
        Check(
            host.Lobby.GetSlotType(7) == SlotType.Private && !host.Lobby.IsSlotEditPending(7),
            "Canceling a checkpoint lost the latest queued slot policy"
        );
        kicked.Lobby.Dispose();
        kicked.Active = false;
        rig.Blocked.Remove(("host", "kicked"));
        var fresh = rig.Add("kicked", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "A fresh explicit join was rejected after an ordinary kick");
        Check(
            fresh.Lobby.Connected && host.Lobby.Roster.Count == 1 && host.Lobby.Roster.Spectator(0) != null,
            "Ordinary kick permanently banned the address"
        );
        rig.Steps(80);
        rig.AssertConfirmedStates();
    }

    private static void LeavingWhileEnteringSpectate()
    {
        using var rig = new Rig();
        var host = rig.Add("host", [new(0, Spawned: true)]);
        var survivor = rig.Add("survivor", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectate-disconnect fixture failed to synchronize");
        var leaving = rig.Add("leaving", [new(0, Spawned: true)]);
        rig.WaitFor(() => rig.Ready, "Spectate-disconnect fixture failed to admit its third peer");
        rig.Steps(80);
        Check(host.Lobby.SetSpectating(leaving.Lobby.LocalPeer, true), "Host could not begin the spectate transition");
        rig.Steps(1);
        leaving.Lobby.Dispose();
        leaving.Active = false;
        rig.WaitFor(
            () => rig.Ready && host.Lobby.PeerIds.Count == 2,
            "Leaving during spectate transition retained the departed active input owner"
        );
        rig.Steps(100);
        Check(
            host.Lobby.PeerIds.SequenceEqual(new[] { 0, survivor.Lobby.LocalPeer }) && host.Lobby.Roster.Count == 2,
            "Spectate transition departure retained a player or spectator"
        );
        rig.AssertConfirmedStates();
    }

    private static bool IsInput(Packet packet) => packet.Data.Length >= 15 && packet.Data[4] == 2;

    private sealed class Rig : IDisposable
    {
        private readonly Random random = new(723);
        private readonly List<Packet> pending = new();
        private readonly Dictionary<(string, string), long> reliableDue = new();
        public readonly List<Node> Nodes = new();
        public readonly List<Packet> Sent = new();
        public int CheckpointMessages;
        public readonly HashSet<(string, string)> Blocked = new();
        public int Frame;
        public long Now => Frame * 1000L / 120;
        public int Delay = 16;
        public int Jitter;
        public double Loss;
        public bool DatagramMode;
        public readonly Dictionary<string, int> DeliveryDelay = new();
        public Func<Node, int, RollbackInput>? Input;
        public bool Ready =>
            Nodes
                .Where(node => node.Active)
                .All(node =>
                    node.Lobby.SimulationReady
                    && !node.Lobby.LocalRequestPending
                    && (
                        node.Lobby.LocalPeer == 0
                        || Nodes[0].Lobby.Roster.Spectator(node.Lobby.LocalPeer) == null
                        || node.Lobby.LobbySession is LobbyNetworkSession { Spectating: true }
                    )
                    && node.Simulation.Membership.Rooms.Select(player =>
                            player == null ? (-1, -1) : (player.Peer, player.Id)
                        )
                        .SequenceEqual(
                            Nodes[0]
                                .Lobby.Roster.Slots.Select(slot =>
                                    slot.Player == null ? (-1, -1) : (slot.Player.Peer, slot.Player.Id)
                                )
                        )
                    && node.Simulation.Membership.Spectators.Select(player => (player.Peer, player.Id))
                        .SequenceEqual(Nodes[0].Lobby.Roster.Spectators.Select(player => (player.Peer, player.Id)))
                    && node.Lobby.LobbySession?.State == GGCS.SessionState.Running
                    && node.Lobby.SessionId == Nodes[0].Lobby.SessionId
                );

        public Node Add(string address, LobbyPlayer[] players, string hash = "mesh-fixture", bool attach = true)
        {
            var wire = new ManualWire(this, address);
            var lobby = new MeshLobby(
                wire,
                Nodes.Count == 0 ? null : Nodes[0].Wire.LocalAddress,
                8,
                players,
                hash,
                "{}"
            );
            var simulation = new LobbySimulation(
                new World(TestFixtures.Map(), new GameRules { Lobby = true, PlayerCount = 8 }, 13),
                lobby.Roster,
                71
            );
            var node = new Node(wire, lobby, simulation);
            Nodes.Add(node);
            if (attach)
                lobby.AttachSimulation(simulation);
            return node;
        }

        public void Steps(int count)
        {
            for (int i = 0; i < count; i++)
                Step();
        }

        public void WaitFor(Func<bool> condition, string failure, int maximumFrames = 2400)
        {
            for (int i = 0; i < maximumFrames && !condition(); i++)
                Step();
            Check(condition(), failure + " " + Describe());
        }

        public string Describe() =>
            string.Join(
                "; ",
                Nodes.Select(node =>
                    $"{node.Wire.LocalAddress}:peer{node.Lobby.LocalPeer},tick{node.Simulation.World.TickNumber},session{node.Lobby.SessionId},status={node.Lobby.Status},wait={node.Lobby.LobbySession?.WaitReason},error={node.Lobby.Error ?? node.Lobby.LobbySession?.Error}"
                )
            );

        private void Step()
        {
            Frame++;
            foreach (var packet in pending.Where(packet => packet.Due <= Now).OrderBy(packet => packet.Due).ToArray())
            {
                pending.Remove(packet);
                var destination = Nodes.FirstOrDefault(node =>
                    node.Active && node.Wire.LocalAddress == packet.Destination
                );
                if (destination != null && !Blocked.Contains((packet.Source, packet.Destination)))
                    destination.Wire.Incoming.Enqueue(new(packet.Source, packet.Data));
            }
            foreach (var node in Nodes.Where(node => node.Active))
                node.Lobby.Poll();
            foreach (var node in Nodes.Where(node => node.Active))
            {
                if (node.Lobby.Error != null)
                {
                    if (!node.AllowError)
                        throw new InvalidOperationException(Describe());
                    continue;
                }
                if (node.Lobby.LobbySession is not { } session)
                    continue;
                session.TryAdvance(
                    session
                        .LocalSlots.Select(handle =>
                            node.Simulation.InputSources[handle].HostCommand
                                ? new RollbackInput(default, Cpu: node.Lobby.HostCommand)
                                : Input?.Invoke(node, handle) ?? default
                        )
                        .ToArray()
                );
                if (session.Error != null)
                    throw new InvalidOperationException(Describe());
            }
        }

        public void AssertConfirmedStates()
        {
            var live = Nodes.Where(node => node.Active && node.Lobby.Error == null).ToArray();
            long stateTick = live.Min(node => node.Lobby.LobbySession!.ConfirmedFrame) + 1;
            byte[]? expected = null;
            foreach (var node in live)
            {
                Check(
                    node.Lobby.LobbySession!.TryGetConfirmedCheckpoint(stateTick, out var state),
                    "Confirmed lobby snapshot unavailable: " + Describe()
                );
                if (expected != null)
                    Check(
                        expected.SequenceEqual(state),
                        "Peers disagree on a confirmed lobby checkpoint: " + Describe()
                    );
                expected = state;
            }
        }

        public void Send(string source, string destination, byte[] data, bool reliable)
        {
            long due = Now + Delay + random.Next(Jitter + 1) + DeliveryDelay.GetValueOrDefault(destination);
            if (reliable)
            {
                due = Math.Max(due, reliableDue.GetValueOrDefault((source, destination)) + 1);
                reliableDue[(source, destination)] = due;
            }
            var packet = new Packet(source, destination, data.ToArray(), reliable, due);
            Sent.Add(packet);
            if (reliable || random.NextDouble() >= Loss)
                pending.Add(packet);
        }

        public void Dispose()
        {
            foreach (var node in Nodes)
                node.Lobby.Dispose();
        }
    }

    private sealed class Node(ManualWire wire, MeshLobby lobby, LobbySimulation simulation)
    {
        public ManualWire Wire = wire;
        public MeshLobby Lobby = lobby;
        public LobbySimulation Simulation = simulation;
        public bool Active = true;
        public bool AllowError;
    }

    private sealed class ManualWire : IWire
    {
        private readonly Rig rig;
        private readonly DatagramReliability? reliability;
        public readonly Queue<WireMessage> Incoming = new();
        public int PollCount { get; private set; }
        public string? Error => null;
        public string LocalAddress { get; }
        public long TimeMilliseconds => rig.Now;

        public ManualWire(Rig rig, string address)
        {
            this.rig = rig;
            LocalAddress = address;
            if (rig.DatagramMode)
                reliability = new DatagramReliability(
                    (destination, data) => rig.Send(address, destination, data, false),
                    () => rig.Now
                );
        }

        public void SetPeers(IReadOnlyCollection<string> addresses) => reliability?.SetPeers(addresses);

        public bool IsConnected(string destination) =>
            rig.Nodes.Any(node => node.Active && node.Wire.LocalAddress == destination)
            && !rig.Blocked.Contains((LocalAddress, destination))
            && (reliability?.IsConnected(destination) ?? true);

        public bool TakeDisconnected(out string address)
        {
            address = "";
            return reliability?.TakeDisconnected(out address) ?? false;
        }

        public void Poll()
        {
            PollCount++;
            if (reliability == null)
                return;
            while (Incoming.TryDequeue(out var packet))
                reliability.Process(packet.Source, packet.Data);
            reliability.Poll();
        }

        public void Send(string destination, byte[] data, bool reliable)
        {
            if (IsCheckpointControl(data))
                rig.CheckpointMessages++;
            if (reliability != null)
                reliability.Send(destination, data, reliable);
            else
                rig.Send(LocalAddress, destination, data, reliable);
        }

        public bool Receive(out WireMessage message) =>
            reliability != null ? reliability.Receive(out message) : Incoming.TryDequeue(out message);

        public void Dispose() { }
    }

    private readonly record struct Packet(string Source, string Destination, byte[] Data, bool Reliable, long Due);
}
