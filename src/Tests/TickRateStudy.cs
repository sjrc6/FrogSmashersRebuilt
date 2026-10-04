using System.Diagnostics;
using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class TickRateStudy
{
    public static void Run()
    {
        var network = new List<object>();
        foreach (int peers in new[] { 2, 8 })
        foreach (int renderRate in new[] { 60, 100, 120, 144, 165, 240 })
        foreach (int roundTripMs in new[] { 100, 300 })
            network.Add(Network(peers, renderRate, roundTripMs));
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    World.TickRate,
                    PredictionMs = RollbackPreferences.PredictionFrames * 1000 / World.TickRate,
                    HistoryMs = RollbackPreferences.HistoryFrames * 1000 / World.TickRate,
                    Cpu = Cpu(),
                    Maneuvers = Maneuvers(),
                    ModeledRenderAndNetwork = network,
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
    }

    private static InputFrame Input(long tick, int slot) => TestFixtures.Input(tick * 120 / World.TickRate, slot);

    private static object Cpu()
    {
        var world = TestFixtures.MakeWorld(8);
        var inputs = new MatchInput[8];
        void Tick()
        {
            for (int slot = 0; slot < inputs.Length; slot++)
                inputs[slot] = new(Input(world.TickNumber, slot));
            world.Advance(inputs);
            _ = world.Capture();
            _ = world.HashState();
        }
        for (int tick = 0; tick < World.TickRate * 10; tick++)
            Tick();
        byte[] snapshot = world.Capture();
        var normal = new List<double>();
        var repair = new List<double>();
        for (int trial = 0; trial < 120; trial++)
        {
            world.Restore(snapshot);
            var timer = Stopwatch.StartNew();
            for (int tick = 0; tick < World.TickRate; tick++)
                Tick();
            if (trial >= 20)
                normal.Add(timer.Elapsed.TotalMilliseconds);
            timer.Restart();
            world.Restore(snapshot);
            for (int tick = 0; tick < World.TickRate / 5; tick++)
                Tick();
            if (trial >= 20)
                repair.Add(timer.Elapsed.TotalMilliseconds);
        }
        normal.Sort();
        repair.Sort();
        return new
        {
            Players = 8,
            NormalMsPerSimulatedSecond = normal[50],
            Repair200MsMedian = repair[50],
            Repair200MsP95 = repair[95],
            SnapshotBytes = snapshot.Length,
        };
    }

    private static object Network(int peers, int renderRate, int roundTripMs)
    {
        const int durationMs = 10000;
        var wire = new SimulatedNetwork(peers, 8721, roundTripMs / 2, 10, 2, 1) { ClockRate = 1000 };
        var slots = Enumerable.Range(0, peers).Select(peer => new[] { peer }).ToArray();
        int delay = World.TickRate / 20;
        var sessions = Enumerable
            .Range(0, peers)
            .Select(peer =>
            {
                var world = TestFixtures.MakeWorld(peers);
                return new NetworkSession(
                    world,
                    new SessionConfig(slots, peer, "timing-study", world, 1, rollback: new() { Delay = delay }),
                    wire.Endpoint(peer)
                );
            })
            .ToArray();
        try
        {
            NetworkSessionTests.Synchronize(wire, sessions);
            long packets = wire.PacketsSent,
                bytes = wire.BytesSent;
            double[] accumulators = new double[peers];
            int stalls = 0,
                emptyFrames = 0,
                maxTicksPerRender = 0,
                frames = 0;
            int lastRenderMs = 0;
            for (int ms = 1; ms <= durationMs; ms++)
            {
                wire.Advance();
                if (ms * renderRate / 1000 == (ms - 1) * renderRate / 1000)
                    continue;
                frames++;
                foreach (var session in sessions)
                {
                    session.Poll();
                    int peer = session.LocalPeer;
                    accumulators[peer] += (ms - lastRenderMs) * World.TickRate / 1000.0;
                    int advances = 0;
                    while (accumulators[peer] + 1e-8 >= session.FrameDurationMultiplier)
                    {
                        double duration = session.FrameDurationMultiplier;
                        if (!session.TryAdvance([Input(session.World.TickNumber, peer)]))
                        {
                            stalls++;
                            accumulators[peer] = Math.Min(accumulators[peer], duration);
                            break;
                        }
                        advances++;
                        accumulators[peer] -= duration;
                    }
                    if (advances == 0)
                        emptyFrames++;
                    maxTicksPerRender = Math.Max(maxTicksPerRender, advances);
                    Check(session.Error == null, session.Error ?? "Timing study failed");
                }
                lastRenderMs = ms;
            }
            double slowestRate = sessions.Min(session => session.World.TickNumber) / (durationMs / 1000.0);
            long replayed = sessions.Sum(session => session.ResimulatedTicks);
            packets = wire.PacketsSent - packets;
            bytes = wire.BytesSent - bytes;
            long target = sessions.Max(session => session.World.TickNumber);
            for (
                int ms = 0;
                ms < 10000 && sessions.Any(s => s.World.TickNumber < target || s.ConfirmedFrame < target - 1);
                ms++
            )
            {
                wire.Advance();
                foreach (var session in sessions)
                {
                    session.Poll();
                    if (session.World.TickNumber < target)
                        session.TryAdvance([Input(session.World.TickNumber, session.LocalPeer)]);
                    Check(session.Error == null, session.Error ?? "Timing study did not recover");
                }
            }
            var baseline = TestFixtures.MakeWorld(peers);
            for (int tick = 0; tick < target; tick++)
                baseline.Advance(
                    Enumerable
                        .Range(0, peers)
                        .Select(slot => Input(tick - delay, slot))
                        .Select(frame => new MatchInput(frame))
                        .ToArray()
                );
            Check(
                sessions.All(s =>
                    s.World.TickNumber == target
                    && s.ConfirmedFrame == target - 1
                    && s.World.HashState() == baseline.HashState()
                ),
                "Timing study must match the offline timeline"
            );
            return new
            {
                Peers = peers,
                RenderHz = renderRate,
                RoundTripMs = roundTripMs,
                InputDelayMs = 50,
                SlowestHz = slowestRate,
                Stalls = stalls,
                PacketsPerSecond = packets / 10.0,
                BytesPerSecond = bytes / 10.0,
                ReplayedTicks = replayed,
                FramesWithoutSimulationPercent = emptyFrames * 100.0 / (frames * peers),
                MaxTicksPerRender = maxTicksPerRender,
                ConfirmedOfflineMatch = true,
            };
        }
        finally
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }

    private static object Maneuvers()
    {
        object Jump(bool held)
        {
            var world = MechanicsFixture.CreateWorld();
            decimal apex = 0;
            int apexTick = 0,
                landedTick = 0;
            for (int tick = 0; tick < World.TickRate * 2; tick++)
            {
                MechanicsFixture.Step(world, new(0, 0, held || tick == 0 ? InputButtons.Jump : 0));
                decimal y = world.Players[0].Y.ToDecimal();
                if (y > apex)
                {
                    apex = y;
                    apexTick = tick + 1;
                }
                if (tick > 0 && world.Players[0].OnGround)
                {
                    landedTick = tick + 1;
                    break;
                }
            }
            return new
            {
                Height = apex,
                ApexMs = apexTick * 1000.0 / World.TickRate,
                LandedMs = landedTick * 1000.0 / World.TickRate,
            };
        }
        var bat = MechanicsFixture.CreateWorld();
        bat.Players[1].X = 4;
        double? hitMs = null;
        for (int tick = 0; tick < World.TickRate; tick++)
        {
            MechanicsFixture.Step(bat, new(0, 0, tick < World.TicksFromSeconds(.025m) ? InputButtons.Attack : 0));
            if (bat.Events.Any(e => e.Kind == SimulationEventKind.Hit))
            {
                hitMs = (tick + 1) * 1000.0 / World.TickRate;
                break;
            }
        }
        var coyote = new List<int>();
        for (int offset = 0; offset <= World.TicksFromSeconds(.2m); offset++)
        {
            var world = MechanicsFixture.CreateWorld();
            var player = world.Players[0];
            player.OnGround = false;
            player.Y = 10;
            player.JumpGraceLeft = Fixed.FromDecimal(.2m);
            MechanicsFixture.Step(world, count: offset);
            MechanicsFixture.Step(world, new(0, 0, InputButtons.Jump));
            if (player.VY > 0)
                coyote.Add(offset);
        }
        var wall = TestFixtures.Map();
        wall.Collision =
        [
            new()
            {
                X = 9,
                Y = 5,
                Width = 2,
                Height = 30,
            },
            new()
            {
                X = 0,
                Y = -1,
                Width = 80,
                Height = 2,
            },
        ];
        var grapple = MechanicsFixture.CreateWorld(map: wall);
        double? latchMs = null;
        for (int tick = 0; tick < World.TickRate / 2; tick++)
        {
            MechanicsFixture.Step(grapple, tick == 0 ? new(0, 0, InputButtons.Tongue) : default);
            if (grapple.Events.Any(e => e.Kind == SimulationEventKind.TongueLatch))
                latchMs ??= (tick + 1) * 1000.0 / World.TickRate;
        }
        var wallJump = MechanicsFixture.CreateWorld(map: wall);
        wallJump.Players[0].X = 7;
        wallJump.Players[0].Y = 8;
        wallJump.Players[0].OnGround = false;
        MechanicsFixture.Step(wallJump, new(1, 0, 0), World.TickRate / 10);
        MechanicsFixture.Step(wallJump, new(1, 0, InputButtons.Jump));
        Check(
            wallJump.Players[0].VX < 0 && wallJump.Players[0].VY > 0 && latchMs.HasValue && hitMs.HasValue,
            "Timing study exercises successful wall jumps, bat hits and terrain grapples"
        );
        return new
        {
            HeldJump = Jump(true),
            TapJump = Jump(false),
            BatHitMs = hitMs,
            GrappleLatchMs = latchMs,
            GrappleXAt500Ms = grapple.Players[0].X.ToDecimal(),
            WallJumpVX = wallJump.Players[0].VX.ToDecimal(),
            WallJumpVY = wallJump.Players[0].VY.ToDecimal(),
            CoyoteLatestInputMs = coyote.Max() * 1000.0 / World.TickRate,
            CoyoteAcceptedTicks = coyote.Count,
        };
    }
}
