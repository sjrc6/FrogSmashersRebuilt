using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class BeachBallTests
{
    public static void Run()
    {
        Spawn();
        Terrain();
        foreach (bool fixes in new[] { false, true })
            Attacks(fixes);
        ApexAndDamping();
        PassiveTransition();
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

    private static void Spawn()
    {
        var world = Create();
        var position = world.BeachBall.Position;
        Step(world, 600);
        Check(
            world.BeachBall.Phase == BeachBallPhase.Resting
                && world.BeachBall.Position == position
                && world.BeachBall.VX == 0
                && world.BeachBall.VY == 0,
            "The ball spawns at rest and stays stationary until hit"
        );
    }

    private static void Terrain()
    {
        var world = Create();
        var ball = world.BeachBall;
        ball.Phase = BeachBallPhase.Flying;
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
        ball.Phase = BeachBallPhase.Settling;
        ball.Y = 10;
        ball.VY = -50;
        Step(world, 3);
        Check(ball.Y >= Fixed.FromDecimal(9.25m) && ball.VY > 0, "Falling balls bounce off one-way tops");
        ball.X = 100;
        Step(world);
        Check(!ball.Active && ball.ResetTicks == World.TickRate, "Out-of-bounds balls enter a reset delay");
        Step(world, World.TickRate);
        Check(world.BeachBall.Active && world.BeachBall.X != 100, "The lobby replaces an out-of-bounds ball");
        Check(
            world.BeachBall.Phase == BeachBallPhase.Resting && world.BeachBall.VX == 0 && world.BeachBall.VY == 0,
            "An out-of-bounds ball respawns at rest"
        );
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
        world.BeachBall.Phase = BeachBallPhase.Passive;
        world.BeachBall.HasReachedApex = true;
        world.BeachBall.SettlingTicks = 3 * World.TickRate;
        Step(world);
        Check(
            world.BeachBall.Phase == BeachBallPhase.Flying
                && !world.BeachBall.HasReachedApex
                && world.BeachBall.SettlingTicks == 0,
            "A new hit resets the passive phase, apex and settling timer"
        );
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
        world.BeachBall.Phase = BeachBallPhase.Flying;
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

    private static World OpenFloor()
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
        world.BeachBall.Phase = BeachBallPhase.Flying;
        return world;
    }

    private static void Settling()
    {
        var world = OpenFloor();
        var ball = world.BeachBall;
        ball.X = 0;
        ball.Y = 5;
        ball.VX = 60;
        ball.VY = 10;
        ball.HitsTaken = 3;
        Step(world, 10);
        Check(
            Fixed.Abs(ball.VX) == 60 && ball.Phase == BeachBallPhase.Flying,
            "Flight has no horizontal drag before settling"
        );
        bool hops = false;
        for (int tick = 0; tick < 1800 && ball.Phase != BeachBallPhase.Resting; tick++)
        {
            Step(world);
            hops |= ball.Phase == BeachBallPhase.Settling && ball.VY > 0;
        }
        Check(
            hops && ball.Phase == BeachBallPhase.Resting && ball.VX == 0 && ball.VY == 0 && ball.HitsTaken == 0,
            "The ball slows horizontally while hopping, comes fully to rest, and clears its combo"
        );
        Check(
            BeachBallState.Radius == Fixed.FromDecimal(1.1m),
            "Ball diameter matches 22 native pixels at 10 pixels per world unit"
        );
    }

    private static World FloorImpact(BeachBallPhase phase, decimal impact, decimal horizontal = 0)
    {
        var world = OpenFloor();
        var ball = world.BeachBall;
        ball.Phase = phase;
        ball.Y = BeachBallState.Radius + Fixed.FromDecimal(.001m);
        ball.VX = Fixed.FromDecimal(horizontal);
        ball.VY = -Fixed.FromDecimal(impact) + 20 * World.TickDuration;
        if (phase != BeachBallPhase.Passive)
        {
            ball.HitsTaken = 4;
            ball.LastHitBy = 3;
        }
        return world;
    }

    private static void ApexAndDamping()
    {
        foreach (
            var (phase, impact, retained) in new[]
            {
                (BeachBallPhase.Flying, 10m, .65m),
                (BeachBallPhase.Settling, 30m, .65m),
                (BeachBallPhase.Settling, 29.9m, .95m),
                (BeachBallPhase.Settling, 1.5m, .95m),
                (BeachBallPhase.Passive, 4.9m, .4m),
                (BeachBallPhase.Passive, 3m, .4m),
            }
        )
        {
            var world = FloorImpact(phase, impact);
            Step(world);
            Check(
                Fixed.Abs(world.BeachBall.VY - Fixed.FromDecimal(impact * retained)) < Fixed.FromDecimal(.00001m),
                $"{phase} retains {retained:P0} of a {impact} vertical floor impact"
            );
            Check(world.BeachBall.Phase == phase, "A bounce alone does not invent an apex or change phase");
        }
        var afterApex = FloorImpact(BeachBallPhase.Flying, 30);
        afterApex.BeachBall.HasReachedApex = true;
        Step(afterApex);
        Check(
            afterApex.BeachBall.Phase == BeachBallPhase.Settling && afterApex.BeachBall.SettlingTicks == 0,
            "First floor contact after an apex starts settling even with a strong impact before one second"
        );
        var beforeApex = FloorImpact(BeachBallPhase.Flying, 3);
        beforeApex.BeachBall.TimeSinceHit = 3;
        Step(beforeApex);
        Check(
            beforeApex.BeachBall.Phase == BeachBallPhase.Flying,
            "A weak floor impact after a long flight cannot settle before an apex"
        );
        var apex = OpenFloor();
        apex.BeachBall.Y = 20;
        apex.BeachBall.VY = Fixed.FromDecimal(.1m);
        Step(apex);
        Check(
            apex.BeachBall.HasReachedApex && apex.BeachBall.Phase == BeachBallPhase.Flying,
            "Gravity crossing upward velocity through zero marks an apex but does not settle in midair"
        );
        apex.BeachBall.Y = BeachBallState.Radius + Fixed.FromDecimal(.001m);
        apex.BeachBall.VY = -10;
        Step(apex);
        Check(apex.BeachBall.Phase == BeachBallPhase.Settling, "Landing after the recorded apex starts settling");

        foreach (int sign in new[] { -1, 1 })
        {
            var world = OpenFloor();
            var ball = world.BeachBall;
            ball.Y = 20;
            ball.Phase = BeachBallPhase.Settling;
            ball.VX = sign * 20;
            Step(world);
            Check(
                Fixed.Abs(ball.VX - sign * 19) < Fixed.FromDecimal(.00001m),
                "Settling removes five percent of horizontal velocity per airborne tick"
            );
            ball.Phase = BeachBallPhase.Flying;
            ball.VX = sign * Fixed.FromDecimal(.99m);
            ball.VY = 10;
            Step(world);
            Check(ball.VX == sign * Fixed.FromDecimal(.99m), "Flying preserves horizontal velocity below one");
            ball.Phase = BeachBallPhase.Settling;
            Step(world);
            Check(
                ball.VX == 0 && ball.VY > 0,
                "Settling stops small horizontal movement without stopping vertical flight"
            );
        }
        var ceilingMap = OpenFloor().Map;
        ceilingMap.Collision.Add(
            new()
            {
                Y = 10,
                Width = 2000,
                Height = 2,
            }
        );
        var ceiling = new World(ceilingMap, new GameRules(lobby: true, playerCount: 8));
        ceiling.BeachBall.Phase = BeachBallPhase.Flying;
        ceiling.BeachBall.Y = 9 - BeachBallState.Radius - Fixed.FromDecimal(.001m);
        ceiling.BeachBall.VY = 1;
        Step(ceiling);
        Check(
            ceiling.BeachBall.VY < 0 && !ceiling.BeachBall.HasReachedApex,
            "A sub-one ceiling bounce preserves vertical motion and does not count as a gravity apex"
        );
    }

    private static void PassiveTransition()
    {
        var world = FloorImpact(BeachBallPhase.Settling, 3);
        var ball = world.BeachBall;
        ball.SettlingTicks = 3 * World.TickRate - 2;
        Step(world);
        Check(
            ball.Phase == BeachBallPhase.Settling && ball.HitsTaken == 4 && ball.LastHitBy == 3,
            "Settling retains combo ownership until three full seconds have elapsed"
        );
        var saved = world.Capture();
        Step(world);
        Check(
            ball.Phase == BeachBallPhase.Passive && ball.HitsTaken == 0 && ball.LastHitBy == -1 && ball.VY != 0,
            "At three seconds with zero horizontal speed the still-bouncing ball becomes passive and clears ownership"
        );
        var expected = world.Capture();
        world.Restore(saved);
        Step(world);
        Check(world.Capture().SequenceEqual(expected), "The settling-to-passive transition restores exactly");
        ball = world.BeachBall;
        for (int tick = 0; tick < 1000 && ball.Phase != BeachBallPhase.Resting; tick++)
            Step(world);
        Check(
            ball.Phase == BeachBallPhase.Resting && ball.VX == 0 && ball.VY == 0,
            "Passive bouncing eventually rests completely"
        );

        var moving = FloorImpact(BeachBallPhase.Settling, 10, 20);
        moving.BeachBall.SettlingTicks = 3 * World.TickRate;
        Step(moving);
        Check(
            moving.BeachBall.Phase == BeachBallPhase.Settling && moving.BeachBall.HitsTaken == 4,
            "Three seconds alone cannot clear ownership while horizontal movement remains"
        );
        moving.BeachBall.VX = Fixed.FromDecimal(1.01m);
        Step(moving);
        Check(
            moving.BeachBall.VX == 0 && moving.BeachBall.Phase == BeachBallPhase.Passive,
            "Settling becomes passive on the tick drag brings horizontal speed below its cutoff"
        );

        var small = FloorImpact(BeachBallPhase.Passive, 2.99m);
        Step(small);
        Check(
            small.BeachBall.Phase == BeachBallPhase.Resting && small.BeachBall.VY == 0,
            "Passive floor impacts below three stop completely"
        );
        var frozen = FloorImpact(BeachBallPhase.Settling, 3);
        frozen.BeachBall.SettlingTicks = 299;
        frozen.BeachBall.HitstopTicks = 2;
        Step(frozen, 2);
        Check(
            frozen.BeachBall.SettlingTicks == 299 && frozen.BeachBall.HitsTaken == 4,
            "Hitstop does not consume settling time or clear ownership"
        );
    }

    private static void Platforms()
    {
        foreach (bool oneWay in new[] { false, true })
        foreach (var phase in new[] { BeachBallPhase.Flying, BeachBallPhase.Settling, BeachBallPhase.Passive })
        {
            var map = TestFixtures.Map();
            map.Collision =
            [
                new()
                {
                    X = 0,
                    Y = 5,
                    Width = 12,
                    Height = 1,
                    OneWay = oneWay,
                },
            ];
            var world = new World(map, new GameRules(lobby: true, playerCount: 8));
            var ball = world.BeachBall;
            ball.Phase = phase;
            ball.Y = 7;
            ball.VY = -200;
            ball.HasReachedApex = true;
            var saved = world.Capture();
            Step(world);
            bool lands = !oneWay || phase != BeachBallPhase.Flying;
            Check(
                lands ? ball.VY > 0 && ball.Y >= Fixed.FromDecimal(6.6m) : ball.VY < 0 && ball.Y < 6,
                $"{phase} {(lands ? "lands on" : "passes down through")} {(oneWay ? "one-way" : "solid")} floors"
            );
            if (oneWay && phase == BeachBallPhase.Flying)
                Check(ball.Phase == phase, "Passing a one-way top after an apex does not begin settling");
            var expected = world.Capture();
            world.Restore(saved);
            Step(world);
            Check(world.Capture().SequenceEqual(expected), "Platform collision and phase restore exactly");

            ball = world.BeachBall;
            ball.Phase = phase;
            ball.HasReachedApex = false;
            ball.Y = 3;
            ball.VY = 400;
            Step(world);
            Check(
                oneWay ? ball.VY > 0 && ball.Y > 6 : ball.VY < 0 && ball.Y < Fixed.FromDecimal(3.4m),
                $"{phase} passes through one-way undersides but bounces off solid ceilings"
            );

            ball.Phase = phase;
            ball.X = -8;
            ball.Y = 5;
            ball.VX = 400;
            ball.VY = 20 * World.TickDuration;
            Step(world);
            Check(
                oneWay ? ball.VX > 0 && ball.X > -6 : ball.VX < 0 && ball.X < -7,
                $"{phase} ignores one-way edges but bounces off solid walls"
            );
        }
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
