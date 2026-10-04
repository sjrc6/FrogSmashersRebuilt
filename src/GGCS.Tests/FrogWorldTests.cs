using System.Diagnostics;
using FrogSmashers.Core;
using GGCS.Core;

namespace GGCS.Tests;

internal static class FrogWorldTests
{
    public static void Run()
    {
        DelayedWorldInputsConverge(2);
        DelayedWorldInputsConverge(8);
        ANewSessionCanStartFromANonzeroWorldCheckpoint();
        MeasureEightPlayerSimulation();
    }

    private static SessionOptions Options() =>
        new()
        {
            FramesPerSecond = World.TickRate,
            MaxPredictionFrames = World.TickRate / 5,
            HistoryFrames = World.TickRate * 4 / 5,
            MaxInputDelay = 4,
        };

    private static void DelayedWorldInputsConverge(int players)
    {
        const int target = World.TickRate * 6;
        var baseline = CreateWorld(players);
        var expected = new ulong[target];
        var generated = new InputFrame[players];
        for (int frame = 0; frame < target; frame++)
        {
            for (int player = 0; player < players; player++)
                generated[player] = Input(frame, player);
            baseline.Tick(generated);
            expected[frame] = baseline.HashState();
        }

        var game = new FrogGame(CreateWorld(players));
        var engine = new RollbackEngine<InputFrame, byte[]>(players, Options(), game);
        var nextInput = new int[players];
        int replayed = 0;
        int stalls = 0;
        int verified = -1;
        for (int wall = 0; wall < target + 400 && engine.ConfirmedFrame < target - 1; wall++)
        {
            for (int player = 0; player < players; player++)
            {
                if (player != 0 && wall is >= 120 and < 170)
                    continue;
                while (nextInput[player] <= engine.CurrentFrame && nextInput[player] < target)
                {
                    int frame = nextInput[player];
                    int delay = player == 0 ? 0 : 5 + (frame * 7 + player * 11) % 8;
                    if (wall < frame + delay)
                        break;
                    engine.AddInput(player, frame, Input(frame, player));
                    nextInput[player]++;
                }
            }

            replayed += engine.Repair();
            if (engine.CurrentFrame < target && !engine.Advance())
                stalls++;
            int confirmed = Math.Min(engine.CurrentFrame - 1, nextInput.Min() - 1);
            engine.SetConfirmedFrame(confirmed);
            for (int frame = verified + 1; frame <= confirmed; frame++)
            {
                Check.True(
                    engine.TryGetChecksum(frame + 1, out ulong actual),
                    "Confirmed world checksum remains retained"
                );
                Check.Equal(
                    expected[frame],
                    actual,
                    $"{players}-player world rollback matches direct simulation at frame {frame}"
                );
            }
            verified = confirmed;
        }

        Check.Equal(target, engine.CurrentFrame, "World progresses after delayed inputs and a burst outage");
        Check.Equal(target - 1, engine.ConfirmedFrame, "All world frames eventually become confirmed");
        Check.Equal(baseline.HashState(), game.World.HashState(), "Final world state equals uninterrupted reference");
        Check.True(replayed > 0, "World scenario exercises rollback");
        Check.True(stalls > 0, "World scenario reaches prediction limit during outage");
        Console.WriteLine(
            $"GGCS world: {players} players, {target} ticks at {World.TickRate} Hz, {replayed} replayed frames, {stalls} waits; all confirmed hashes match."
        );
    }

    private static void ANewSessionCanStartFromANonzeroWorldCheckpoint()
    {
        const int checkpointTick = 137;
        const int target = 160;
        var baseline = CreateWorld(8);
        var inputs = new InputFrame[8];
        for (int tick = 0; tick < checkpointTick; tick++)
        {
            for (int player = 0; player < 8; player++)
                inputs[player] = Input(tick, player);
            baseline.Tick(inputs);
        }
        byte[] checkpoint = baseline.Capture();
        ulong checkpointHash = World.Hash(checkpoint);
        var game = new FrogGame(CreateWorld(8));
        game.World.Restore(checkpoint);
        game.StartTick = game.World.TickNumber;
        var engine = new RollbackEngine<InputFrame, byte[]>(8, Options(), game);
        for (int frame = 0; frame < target; frame++)
        {
            for (int player = 0; player < 8; player++)
            {
                inputs[player] = Input(frame + checkpointTick, player);
                if (player < 7)
                    engine.AddInput(player, frame, inputs[player]);
            }
            baseline.Tick(inputs);
            Check.True(engine.Advance(), "A fresh session predicts after restoring a world checkpoint");
            engine.AddInput(7, frame, inputs[7]);
            engine.Repair();
            engine.SetConfirmedFrame(frame);
            Check.Equal(
                baseline.HashState(),
                game.World.HashState(),
                "Session-relative frames preserve the world's original tick counter"
            );
        }
        Check.Equal(
            (long)(checkpointTick + target),
            game.World.TickNumber,
            "Checkpoint restart keeps world tick numbering"
        );
        Check.Equal(checkpointHash, World.Hash(checkpoint), "Loading the world never mutates checkpoint bytes");
    }

    private static void MeasureEightPlayerSimulation()
    {
        Measure(128, 0);
        var normal = Measure(1200, 0);
        var rollback = Measure(48, World.TickRate / 5);
        Console.WriteLine(
            $"GGCS world timing (8 players): normal {normal.ElapsedMilliseconds / normal.Samples:F3} ms/tick, "
                + $"{normal.AllocatedBytes / normal.Samples:F0} B/tick; 200 ms repair {rollback.ElapsedMilliseconds / rollback.Samples:F3} ms/repair, "
                + $"{rollback.AllocatedBytes / rollback.Samples:F0} B/repair. Includes World snapshot/hash work; environment-specific measurements."
        );
    }

    private static Measurement Measure(int samples, int rollbackDepth)
    {
        var game = new FrogGame(CreateWorld(8));
        var engine = new RollbackEngine<InputFrame, byte[]>(8, Options(), game);
        double elapsedMilliseconds = 0;
        long allocatedBytes = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            if (rollbackDepth == 0)
            {
                for (int player = 0; player < 8; player++)
                    engine.AddInput(player, sample, Input(sample, player));
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                Check.True(engine.Advance(), "Benchmark normal frame advances");
                engine.SetConfirmedFrame(sample);
                elapsedMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            }
            else
            {
                int firstFrame = engine.CurrentFrame;
                for (int frame = firstFrame; frame < firstFrame + rollbackDepth; frame++)
                {
                    for (int player = 0; player < 7; player++)
                        engine.AddInput(player, frame, Input(frame, player));
                    Check.True(engine.Advance(), "Benchmark fills full prediction window");
                }
                for (int frame = firstFrame; frame < firstFrame + rollbackDepth; frame++)
                {
                    sbyte direction = (sbyte)(sample % 2 == 0 ? 1 : -1);
                    engine.AddInput(7, frame, new InputFrame(direction, 0, InputButtons.Jump));
                }
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                Check.Equal(rollbackDepth, engine.Repair(), "Benchmark performs complete 24-frame correction");
                engine.SetConfirmedFrame(engine.CurrentFrame - 1);
                elapsedMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            }
        }
        return new Measurement(samples, elapsedMilliseconds, allocatedBytes);
    }

    private readonly record struct Measurement(double Samples, double ElapsedMilliseconds, long AllocatedBytes);

    private static World CreateWorld(int players) =>
        new(
            new MapData
            {
                Id = "ggcs-test",
                Name = "Rollback test arena",
                KillBounds = new BoundsData
                {
                    Left = -35,
                    Right = 35,
                    Bottom = -24,
                    Top = 60,
                },
                Collision =
                [
                    new BoxData
                    {
                        X = 0,
                        Y = -1,
                        Width = 42,
                        Height = 2,
                    },
                    new BoxData
                    {
                        X = -8,
                        Y = 8,
                        Width = 12,
                        Height = 1,
                        OneWay = true,
                    },
                    new BoxData
                    {
                        X = 17,
                        Y = 7,
                        Width = 2,
                        Height = 16,
                    },
                ],
                Spawns = Enumerable
                    .Range(0, 8)
                    .Select(player => new PointData { X = -14 + player * 4, Y = 2 })
                    .ToList(),
                FlySpawn = new PointData { X = 0, Y = 12 },
            },
            new GameRules
            {
                PlayerCount = players,
                WinScore = 99,
                MatchRounds = 3,
            },
            12345
        );

    private static InputFrame Input(int frame, int player)
    {
        uint random = unchecked((uint)(frame / 18 + player * 71 + 12345));
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        InputButtons buttons = 0;
        if ((frame + player * 13) % 59 < 16)
            buttons |= InputButtons.Jump;
        if ((frame + player * 19) % 131 is > 40 and < 80)
            buttons |= InputButtons.Attack;
        if ((frame + player * 7) % 157 is > 90 and < 98)
            buttons |= InputButtons.Tongue;
        if ((frame + player * 11) % 103 is > 25 and < 55)
            buttons |= InputButtons.Strafe;
        return new InputFrame((sbyte)((int)(random % 3) - 1), (sbyte)((int)((random >> 4) % 3) - 1), buttons);
    }

    private sealed class FrogGame(World world) : IRollbackGame<InputFrame, byte[]>
    {
        private readonly InputFrame[] inputs = new InputFrame[world.Players.Length];
        public World World { get; } = world;
        public long StartTick { get; set; } = world.TickNumber;

        public SavedState<byte[]> SaveState()
        {
            byte[] state = World.Capture();
            return new SavedState<byte[]>(state, World.Hash(state));
        }

        public void LoadState(byte[] state) => World.Restore(state);

        public void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<InputFrame>> values, bool isResimulation)
        {
            Check.Equal(StartTick + frame, World.TickNumber, "World tick aligns with session state boundary");
            for (int player = 0; player < values.Length; player++)
                inputs[player] = values[player].Input;
            World.Tick(inputs);
        }
    }
}
