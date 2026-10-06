using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class PhysicsTests
{
    public static void FixedArithmetic()
    {
        Check(
            Fixed.Sqrt(FromDecimal(2)) * Fixed.Sqrt(FromDecimal(2)) > FromDecimal(1.99999m),
            "integer square root retains fixed precision"
        );
        Check(
            FromDecimal(200000) * FromDecimal(200000) == FromDecimal(40000000000m)
                && Fixed.Sqrt(FromDecimal(40000000000m)) == FromDecimal(200000),
            "wide fixed multiplication and square root retain precision beyond 64-bit intermediate range"
        );
        bool overflow = false;
        try
        {
            _ = -new Fixed(long.MinValue);
        }
        catch (OverflowException)
        {
            overflow = true;
        }

        Check(overflow, "fixed negation rejects the unrepresentable minimum-value magnitude");
    }

    public static void LongRunningClock()
    {
        var longRunning = CreateWorld();
        longRunning.Fly.Active = true;
        longRunning.Fly.Y = 5;
        longRunning.Fly.DirectionTicks = 1000;
        var future = longRunning.Capture();
        long futureTick = 120L * 60 * 60 * 24 * 365 * 100;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(future.AsSpan(16, 8), futureTick);
        longRunning.Restore(future);
        Step(longRunning);
        Check(
            longRunning.TickNumber == futureTick + 1
                && longRunning.Fly.Y > FromDecimal(4.9m)
                && longRunning.Fly.Y < FromDecimal(5.1m),
            "hundred-year tick clocks retain bounded fly motion without intermediate overflow"
        );
    }

    public static void ContentTuning()
    {
        var content = new GameContent
        {
            Maps = [TestFixtures.Map()],
            CharacterParameters = new Dictionary<string, decimal> { ["maxRunSpeed"] = 7, ["jumpVel"] = 13 },
        };
        var world = new World(content, new GameRules(), 123);
        foreach (var player in world.Players)
        {
            player.Alive = true;
            player.OnGround = true;
            player.X = player.Slot * 20;
        }
        Step(world, new InputFrame(1, 0, InputButtons.None), 30);
        Check(world.Players[0].VX == 7, "loaded character tuning controls the run speed cap");
        Step(world, new InputFrame(0, 0, InputButtons.Jump));
        Check(world.Players[0].VY == 13, "loaded character tuning controls jump velocity");
    }
}
