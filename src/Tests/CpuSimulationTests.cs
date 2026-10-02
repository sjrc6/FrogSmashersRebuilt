using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class CpuSimulationTests
{
    public static void Run()
    {
        LobbyCommandsRestoreExactly();
        CpuMatchesRecomputeDuringRollback(false, false);
        CpuMatchesRecomputeDuringRollback(true, false);
        CpuMatchesRecomputeDuringRollback(true, true);
    }

    private static void LobbyCommandsRestoreExactly()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0, Spawned: true)]);
        var simulation = new LobbySimulation(new World(Map(), new GameRules { Lobby = true, PlayerCount = 8 }), roster);
        var handles = simulation.InputSources.ToArray();
        byte[] before = simulation.Capture();
        roster.Edit(7, SlotType.Cpu);
        var add = LobbyCpuCommand.FromRoster(1, 128, roster);
        var codec = new RollbackInputCodec();
        byte[] encoded = new byte[codec.Size];
        codec.Encode(new(default, Cpu: add), encoded);
        Check(codec.Decode(encoded).Cpu == add, "CPU commands survive the wire codec exactly");
        foreach (
            var invalid in new LobbyCpuCommand[]
            {
                new(-1, 1, 1, 0, 0),
                new(1, 1, 2, 0, 0),
                new(1, 1, 1, 1u << 24, 0),
                new(0, 1, 1, 0, 0),
            }
        )
        {
            bool invalidRejected = false;
            try
            {
                codec.Encode(new(default, Cpu: invalid), encoded);
            }
            catch (InvalidDataException)
            {
                invalidRejected = true;
            }
            Check(invalidRejected, "Invalid CPU command was accepted by the codec");
        }
        simulation.Tick([new(default, Cpu: add), default]);
        Check(simulation.Membership.Rooms[7] is { Cpu: true, Spawned: true }, "CPU command did not spawn a frog");
        byte[] after = simulation.Capture();
        simulation.World.Players[7].X = 12;
        simulation.World.Players[7].Y = 20;
        simulation.Tick([new(default, Cpu: add), default]);
        Check(simulation.World.Players[7].Y > 19, "Repeating a command respawned the CPU");
        simulation.Restore(before);
        simulation.Tick([new(default, Cpu: add), default]);
        Check(after.SequenceEqual(simulation.Capture()), "Rollback did not reproduce CPU creation");

        var configure = new LobbyCpuCommand(2, 128, 128, 6u << 21, 5u << 21);
        simulation.World.Players[7].Y = 20;
        simulation.Tick([new(default, Cpu: configure), default]);
        Check(simulation.Membership.Rooms[7] is { Color: 6, Team: 5 }, "CPU configuration was not applied");
        Check(simulation.World.Players[7].Y > 19, "Configuring a CPU reset its body");
        simulation.World.Players[0].LastHitBy = 7;
        var remove = new LobbyCpuCommand(3, 128, 0, 0, 0);
        simulation.Tick([new(default, Cpu: remove), default]);
        Check(
            simulation.Membership.Rooms[7] == null && simulation.World.Players[7].Eliminated,
            "CPU removal left an active body"
        );
        Check(simulation.World.Players[0].LastHitBy == -1, "Removed CPU retained credit for future deaths");
        simulation.Tick([new(default, Cpu: add), default]);
        Check(simulation.Membership.Rooms[7] == null, "An obsolete command restored a removed CPU");
        Check(handles.SequenceEqual(simulation.InputSources), "CPU commands changed human input identities");

        byte[] unchanged = simulation.Capture();
        bool rejected = false;
        try
        {
            simulation.Tick([default, new(default, Cpu: configure)]);
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Check(rejected && unchanged.SequenceEqual(simulation.Capture()), "A human stream could issue host commands");
        simulation.Tick([new(default, Cpu: new(4, 1, 1, 1, 0)), default]);
        Check(simulation.Membership.Rooms[0] is { Cpu: false }, "A CPU command replaced a human frog");
    }

    private static void CpuMatchesRecomputeDuringRollback(bool hostSpectates, bool allCpus)
    {
        bool[] cpus = [true, allCpus, true, allCpus, false, false, false, false];
        World Create() =>
            new(
                Map(),
                new GameRules
                {
                    PlayerCount = 4,
                    CpuPlayers = cpus,
                    WinScore = 999,
                },
                42
            );
        int[] bodies = allCpus ? [-1] : [-1, 1, 3];
        int[][] owners =
            allCpus
                ?
                [
                    [0],
                    [],
                ]
            : hostSpectates
                ?
                [
                    [0],
                    [1, 2],
                ]
            :
            [
                [0, 1],
                [2],
            ];
        var wire = new SimulatedNetwork(2, 714, 9, 5, 8, 4);
        var sessions = Enumerable
            .Range(0, 2)
            .Select(peer =>
            {
                var world = Create();
                return new NetworkSession(
                    world,
                    new SessionConfig(
                        owners,
                        peer,
                        "cpu-match",
                        world,
                        1,
                        rollback: FixedTiming,
                        inputPlayerSlots: bodies
                    ),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        const int ticks = 600;
        bool cpuMoved = false;
        try
        {
            for (
                int wall = 0;
                wall < 6000 && sessions.Any(s => s.World.TickNumber < ticks || s.ConfirmedFrame < ticks - 1);
                wall++
            )
            {
                wire.Advance();
                foreach (var session in sessions)
                {
                    session.Poll();
                    Check(session.Error == null, session.Error ?? "CPU match failed");
                    if (session.World.TickNumber < ticks)
                        session.TryAdvance(
                            session
                                .LocalSlots.Select(handle =>
                                    bodies[handle] < 0 ? default : Input(session.World.TickNumber, bodies[handle])
                                )
                                .ToArray()
                        );
                    cpuMoved |= session
                        .World.Players.Where((_, slot) => cpus[slot])
                        .Any(player => player.PreviousInput != default);
                }
            }
            var expected = Create();
            for (int tick = 0; tick < ticks; tick++)
                expected.Tick(
                    Enumerable
                        .Range(0, 4)
                        .Select(slot => cpus[slot] ? default : Input(tick - FixedTiming.Delay, slot))
                        .ToArray()
                );
            Check(cpuMoved, "Match CPUs did not generate their own inputs");
            foreach (var session in sessions)
            {
                Check(
                    session.World.TickNumber == ticks && session.ConfirmedFrame == ticks - 1,
                    "CPU match did not finish"
                );
                Check(
                    expected.Capture().SequenceEqual(session.World.Capture()),
                    "Recomputed CPU AI diverged from confirmed human inputs"
                );
                Check(
                    session.InputHandle(0) == -1 && session.InputHandle(2) == -1,
                    "CPU acquired a network input handle"
                );
            }
            Check(allCpus || hostSpectates || sessions.Sum(s => s.RollbackCount) > 0, "CPU fixture never rolled back");
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }
}
