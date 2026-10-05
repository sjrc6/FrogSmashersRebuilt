using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class BeachBallTests
{
    public static void Run()
    {
        Terrain();
        foreach (bool fixes in new[] { false, true })
            Attacks(fixes);
        Settling();
        Platforms();
        Replay();
        Console.WriteLine("Beach ball terrain, attacks, reset, harmless lobby combat and replay passed");
    }

    private static World Create(bool fixes = true) =>
        new(
            TestFixtures.Map(),
            new GameRules(playerCount: 8, lobby: true, modifiers: new GameModifiers { PhysicsFixes = fixes })
        );

    private static void Step(World world, int ticks = 1)
    {
        for (int tick = 0; tick < ticks; tick++)
            world.Advance(new MatchInput[8]);
    }

    private static void Terrain()
    {
        var world = Create();
        var ball = world.BeachBall;
        ball.X = 0;
        ball.Y = 1;
        ball.VY = -50;
        Step(world);
        Check(ball.Y >= BeachBallState.Radius && ball.VY > 0, "Fast ball bounces from solid ground without tunneling");
        ball.X = 14;
        ball.Y = 4;
        ball.VX = 200;
        ball.VY = 0;
        Step(world);
        Check(ball.X < 16 && ball.VX < 0, "Ball sweeps into vertical walls");
        ball.X = -8;
        ball.Y = Fixed.FromDecimal(7.5m);
        ball.VX = 0;
        ball.VY = 50;
        Step(world, 5);
        Check(ball.Y > 9 && ball.VY > 0, "One-way platforms let rising balls pass through");
        ball.Y = 10;
        ball.VY = -50;
        Step(world, 3);
        Check(ball.Y >= Fixed.FromDecimal(9.25m) && ball.VY > 0, "Falling balls bounce off one-way tops");
        ball.X = 100;
        Step(world);
        Check(!ball.Active && ball.ResetTicks == World.TickRate, "Out-of-bounds balls enter a reset delay");
        Step(world, World.TickRate);
        Check(world.BeachBall.Active && world.BeachBall.X != 100, "The lobby replaces an out-of-bounds ball");
        byte[] before = world.Capture();
        Step(world, 100);
        byte[] expected = world.Capture();
        world.Restore(before);
        Step(world, 100);
        Check(world.Capture().SequenceEqual(expected), "Ball motion restores and resimulates exactly");
        var roster = new LobbyRoster();
        var simulation = new LobbySimulation(world, roster);
        roster.SetPlayers(0, [new(4)]);
        var position = world.BeachBall.Position;
        simulation.ApplyRoster(roster);
        Check(world.BeachBall.Position == position, "A new device does not reset the beach ball");
    }

    private static void Attacks(bool fixes)
    {
        var world = Create(fixes);
        world.SetLobbySlot(0, true, 0);
        world.SetLobbySlot(1, true, 1);
        Step(world);
        var hitter = world.Players[0];
        var other = world.Players[1];
        hitter.X = 0;
        hitter.Y = 0;
        hitter.Facing = 1;
        other.X = 4;
        other.Y = 0;
        hitter.Mode = CharacterMode.Attacking;
        hitter.AttackPhase = AttackPhase.Swing;
        hitter.AttackTimeLeft = 0;
        hitter.AttackX = 1;
        hitter.AttackCharge = Fixed.FromDecimal(.5m);
        world.BeachBall.X = 4;
        world.BeachBall.Y = 2;
        Step(world);
        Check(
            world.BeachBall.VX > 20 && world.Events.Any(e => e.Kind == SimulationEventKind.BeachBallHit),
            "Bat strikes launch the ball"
        );
        Check(
            other.HitsTaken == 0 && other.LastHitBy == -1 && other.Mode == CharacterMode.Normal,
            "Lobby bat contact never damages or launches another frog"
        );
        Check(
            world.Events.Any(e => e.Kind == SimulationEventKind.LobbyContact),
            "Harmless contact is available for CPU activation"
        );
        var ball = world.BeachBall;
        Check(
            ball.HitsTaken == 1 && ball.LastHitBy == 0 && ball.HitstopTicks > 0 && hitter.HitstopTicks > 0,
            "Ball hits share combo ownership and attacker/victim hitstop with frog hits"
        );
        var frozen = ball.Position;
        Step(world, 3);
        Check(ball.Position == frozen, "Hitstop freezes ball motion");
        var firstSpeed = new FixedVector(ball.VX, ball.VY).Length;
        hitter.HitstopTicks = ball.HitstopTicks = 0;
        hitter.AttackPhase = AttackPhase.Swing;
        hitter.AttackTimeLeft = 0;
        ball.X = 4;
        ball.Y = 2;
        Step(world);
        Check(
            ball.HitsTaken == 2 && new FixedVector(ball.VX, ball.VY).Length > firstSpeed,
            "Consecutive bat hits increase the ball's knockback"
        );
        var saved = world.Capture();
        Step(world, 100);
        var expected = world.Capture();
        world.Restore(saved);
        Step(world, 100);
        Check(world.Capture().SequenceEqual(expected), "Combo, freeze, flight and bounce restore exactly");
        hitter = world.Players[0];
        other = world.Players[1];
        hitter.X = hitter.Y = 0;
        hitter.HitstopTicks = world.BeachBall.HitstopTicks = 0;
        world.BeachBall.Resting = false;
        other.X = 20;
        world.BeachBall.X = 5;
        world.BeachBall.Y = 1;
        world.BeachBall.VX = world.BeachBall.VY = 0;
        hitter.Mode = CharacterMode.Tongue;
        hitter.TonguePhase = TonguePhase.Extending;
        hitter.TongueX = 1;
        hitter.TongueY = 0;
        hitter.TongueDistance = 3;
        hitter.TongueDelayLeft = 0;
        bool touched = false;
        for (int tick = 0; tick < 10 && !touched; tick++)
        {
            Step(world);
            touched = world.Events.Any(e => e.Kind == SimulationEventKind.BeachBallHit);
        }
        Check(touched && world.BeachBall.VX < 0, "Tongues pull the ball in either physics mode");
    }

    private static void Settling()
    {
        var map = new MapData
        {
            Id = "settling",
            Spawns = TestFixtures.Map().Spawns,
            Collision =
            [
                new()
                {
                    Y = -1,
                    Width = 2000,
                    Height = 2,
                },
            ],
            KillBounds = new()
            {
                Left = -1000,
                Right = 1000,
                Bottom = -100,
                Top = 200,
            },
        };
        var world = new World(map, new GameRules(lobby: true, playerCount: 8));
        var ball = world.BeachBall;
        ball.X = 0;
        ball.Y = 5;
        ball.VX = 60;
        ball.VY = 10;
        ball.HitsTaken = 3;
        Step(world, 10);
        Check(Fixed.Abs(ball.VX) == 60 && !ball.Settling, "Flight has no horizontal drag before settling");
        bool hops = false;
        for (int tick = 0; tick < 1800 && !ball.Resting; tick++)
        {
            Step(world);
            hops |= ball.Settling && ball.VY > 0;
        }
        Check(
            hops && ball.Resting && ball.VX == 0 && ball.VY == 0 && ball.HitsTaken == 0,
            "The ball slows horizontally while hopping, comes fully to rest, and clears its combo"
        );
        Check(BeachBallState.Radius == Fixed.FromDecimal(.8625m), "Ball radius is 15 percent larger");
    }

    private static void Platforms()
    {
        var map = TestFixtures.Map();
        map.Collision.Add(
            new()
            {
                X = 0,
                Y = 5,
                Width = 12,
                Height = 1,
                BeachBallCollision = false,
            }
        );
        var world = new World(map, new GameRules(lobby: true, playerCount: 8));
        world.BeachBall.Y = 8;
        world.BeachBall.VY = -30;
        Step(world, 15);
        Check(world.BeachBall.Y < 4 && world.BeachBall.VY < 0, "Disabled interior platforms do not block the ball");
        Step(world, 15);
        Check(world.BeachBall.Y >= BeachBallState.Radius, "The solid outer floor still stops the ball");
    }

    private static void Replay()
    {
        var world = Create();
        world.SetLobbySlot(0, true, 0);
        var replay = InputReplay.Start(world);
        for (int tick = 0; tick < 600; tick++)
        {
            var input = Enumerable.Range(0, 8).Select(slot => new MatchInput(TestFixtures.Input(tick, slot))).ToArray();
            world.Advance(input);
            replay.Record(input, world);
        }
        var restored = Create();
        replay.Play(restored);
        Check(
            world.HashState() == restored.HashState(),
            "Beach ball simulation and lobby attacks replay to the same hash"
        );
    }
}
