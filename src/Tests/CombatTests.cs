using FrogSmashers.Core;
using static FrogSmashers.Core.Fixed;
using static FrogSmashers.Tests.MechanicsFixture;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class CombatTests
{
    public static void BatHitsAndHitstop()
    {
        var bat = CreateWorld();
        bat.Players[1].X = 4;
        Step(bat, new(0, 0, InputButtons.Attack), 3);
        SimulationEvent? firstHit = null;
        for (int i = 0; i < 4; i++)
        {
            Step(bat);
            foreach (var e in bat.Events)
            {
                if (e.Kind == SimulationEventKind.Hit)
                {
                    firstHit = e;
                }
            }
        }

        Check(
            bat.Players[1].HitsTaken == 1 && bat.Players[1].Mode == CharacterMode.Bouncing && bat.Players[1].VX > 0,
            "released bat swing hits in a swept capsule and applies knockback"
        );
        Check(
            firstHit is { ComboHits: 1, HitKind: HitKind.Bat }
                && firstHit.Value.Power < 1
                && firstHit.Value.Strength > 20
                && firstHit.Value.VelocityX > 0,
            "hit events distinguish charge, combo and launch speed for faithful audio and effects"
        );
        Check(
            firstHit.HasValue
                && firstHit.Value.HitstopSeconds > FromDecimal(.17m)
                && firstHit.Value.HitEffectX == firstHit.Value.X
                && firstHit.Value.HitEffectY == firstHit.Value.Y + 1,
            "bat effects carry the actual hitstop duration and original center anchor"
        );
        var frozen = bat.Players[1].Position;
        Step(bat, count: 8);
        Check(
            bat.Players[1].Position == frozen && bat.Players[1].HitstopTicks > 0,
            "hitstop freezes the struck player"
        );
        var bouncer = CreateWorld();
        var source = bouncer.Players[0];
        var target = bouncer.Players[1];
        source.Y = 10;
        source.OnGround = false;
        source.Mode = CharacterMode.Bouncing;
        source.HitsTaken = 2;
        source.VX = 12;
        source.VY = 10;
        target.X = 1;
        target.Y = 10;
        target.OnGround = false;
        target.HitstopTicks = 120;
        target.HitstopScale = 0;
        var beforeBounce = bouncer.Capture();
        Step(bouncer);
        Check(
            target.HitsTaken == 0
                && target.Mode != CharacterMode.Bouncing
                && source.HitstopTicks == 0
                && !bouncer.Events.Any(e => e.Kind == SimulationEventKind.Hit),
            "launched frogs pass through other frogs without body hits or transferred hitstop"
        );
        bouncer.Restore(beforeBounce);
        bouncer.Players[0].HasReachedApex = true;
        Step(bouncer);
        Check(
            !bouncer.Events.Any(e => e.Kind == SimulationEventKind.Hit),
            "body contact also causes no hit after the launch apex"
        );
        var launch = CreateWorld();
        var launched = launch.Players[0];
        launched.Y = 10;
        launched.OnGround = false;
        launched.Mode = CharacterMode.Bouncing;
        launched.HitsTaken = 5;
        launched.HitstopTicks = 2;
        launched.HitstopScale = 0;
        launched.VX = 50;
        Step(launch);
        Check(
            !launch.Events.Any(e => e.Kind == SimulationEventKind.Launch),
            "high-combo launch audio waits through full hitstop"
        );
        var preLaunch = launch.Capture();
        Step(launch);
        var launchEvent = launch.Events.Single(e => e.Kind == SimulationEventKind.Launch);
        Check(
            launchEvent.ComboHits == 5 && launchEvent.VelocityX == 50,
            "high-combo launch carries its source combo and velocity"
        );
        launch.Restore(preLaunch);
        Step(launch);
        Check(
            launch.Events.Single(e => e.Kind == SimulationEventKind.Launch) == launchEvent,
            "launch release event repeats identically after rollback"
        );
        Step(launch);
        Check(
            !launch.Events.Any(e => e.Kind == SimulationEventKind.Launch),
            "launch audio is emitted once per freeze release"
        );
        launch.Restore(preLaunch);
        launch.Players[0].HasBounceDodged = true;
        Step(launch);
        Check(
            !launch.Events.Any(e => e.Kind == SimulationEventKind.Launch),
            "bounce dodge suppresses launch audio as in original animation logic"
        );
        launch.Restore(preLaunch);
        launch.Players[0].HitsTaken = 4;
        Step(launch);
        Check(
            !launch.Events.Any(e => e.Kind == SimulationEventKind.Launch),
            "lower combos do not play the original level-three launch audio"
        );
        var downCombo = CreateWorld();
        downCombo.Players[1].X = 4;
        downCombo.Players[1].WasHitDownwards = true;
        Step(downCombo, new(0, 0, InputButtons.Attack), 3);
        Step(downCombo, count: 4);
        Check(
            downCombo.Players[1].HitsTaken == 1 && downCombo.Players[1].WasHitDownwards,
            "later horizontal combo hits retain downward platform bypass until recovery"
        );
        var team = CreateWorld(new(playerCount: 3, format: MatchFormat.Teams, teams: [0, 0, 1]));
        team.Players[1].X = 4;
        Step(team, new(0, 0, InputButtons.Attack), 3);
        Step(team, count: 4);
        Check(team.Players[1].HitsTaken == 0, "bat attacks do not damage teammates");
    }

    public static void TongueAttacks()
    {
        var tongue = CreateWorld();
        tongue.Players[1].X = 6;
        Step(tongue, new(1, 0, InputButtons.Tongue), 25);
        Check(
            tongue.Players[1].Mode == CharacterMode.Bouncing
                && tongue.Players[1].VX < 0
                && tongue.Players[1].HitsTaken == 0,
            "tongue pulls the target without adding a bat combo hit"
        );
        var grapple = CreateWorld(
            map: new()
            {
                Id = "grapple",
                Name = "grapple",
                Collision =
                [
                    new()
                    {
                        X = 9,
                        Y = 5,
                        Width = 2,
                        Height = 20,
                    },
                    new()
                    {
                        X = 0,
                        Y = -1,
                        Width = 80,
                        Height = 2,
                    },
                ],
            }
        );
        Step(grapple, new(1, 0, InputButtons.Tongue), 35);
        Check(grapple.Players[0].X > 2, "tongue attaches to solid terrain and pulls the frog");
    }

    public static void FlyClaims()
    {
        var fly = CreateWorld();
        fly.Fly.Active = true;
        fly.Fly.X = 5;
        fly.Fly.Y = FromDecimal(1.5m);
        fly.Fly.DirectionTicks = 10000;
        Step(fly, new(1, 0, InputButtons.Tongue), 100);
        Check(
            fly.Players[0].HasFly && !fly.Fly.Active && fly.Fly.IngestedBy == 0,
            "tongue claims, retracts and ingests a fly"
        );
        var orphan = CreateWorld();
        orphan.Fly.Active = true;
        orphan.Fly.Owner = 0;
        orphan.Fly.ClaimTicks = 100;
        orphan.Players[0].Mode = CharacterMode.Normal;
        Step(orphan);
        Check(orphan.Fly.Owner == -1, "orphaned fly claims release immediately");
        var timeout = CreateWorld();
        timeout.Fly.Active = true;
        timeout.Fly.Owner = 0;
        timeout.Fly.ClaimTicks = 1;
        timeout.Players[0].Mode = CharacterMode.Tongue;
        timeout.Players[0].TonguePhase = TonguePhase.RetractingHitFly;
        timeout.Players[0].TongueDistance = 5;
        timeout.Players[0].TongueDelayLeft = 10;
        Step(timeout);
        Check(timeout.Fly.Owner == -1, "fly claim watchdog expires even for a valid owner");
    }

    public static void StrafeInputs()
    {
        foreach (sbyte x in new sbyte[] { -1, 0, 1 })
        {
            foreach (sbyte y in new sbyte[] { -1, 0, 1 })
            {
                for (int buttons = 0; buttons <= 15; buttons++)
                {
                    var input = new InputFrame(x, y, (InputButtons)buttons);
                    Check(
                        InputFrame.FromPacked(input.Packed) == input,
                        "all directions and strafe/action combinations survive packed input roundtrip"
                    );
                }
            }
        }

        bool rejected = false;
        try
        {
            InputFrame.FromPacked(16u << 16);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Check(rejected, "packed input rejects the first undefined button bit after Strafe");
        var strafe = CreateWorld();
        var normal = CreateWorld();
        Step(strafe, new(-1, 0, InputButtons.Strafe), 24);
        Step(normal, new(-1, 0, 0), 24);
        Check(
            strafe.Players[0].Facing == 1
                && normal.Players[0].Facing == -1
                && strafe.Players[0].Position == normal.Players[0].Position
                && strafe.Players[0].Velocity == normal.Players[0].Velocity,
            "fork strafe freezes facing without changing running acceleration or position"
        );
        Step(strafe, new(-1, 0, InputButtons.Strafe | InputButtons.Attack), 3);
        Check(
            strafe.Players[0].AttackX == 1 && strafe.Players[0].Facing == 1,
            "horizontal bat charge keeps its facing while strafing backwards"
        );
        Step(strafe, new(-1, 1, InputButtons.Strafe | InputButtons.Attack));
        Check(
            strafe.Players[0].AttackX == -1 && strafe.Players[0].AttackY == 1 && strafe.Players[0].Facing == 1,
            "original diagonal bat aiming remains directional while strafe freezes facing"
        );
        var rebound = CreateWorld();
        var r = rebound.Players[0];
        r.Mode = CharacterMode.Bouncing;
        r.Y = 10;
        r.OnGround = false;
        r.VX = -30;
        r.VY = 10;
        Step(rebound, new(0, 0, InputButtons.Strafe));
        Check(r.Facing == 1 && r.VX < 0, "strafe suppresses bounce velocity-driven turning");
        Step(rebound);
        Check(r.Facing == -1, "releasing strafe restores bounce velocity-driven turning");
        var simultaneous = CreateWorld();
        Step(
            simultaneous,
            new(1, 0, InputButtons.Jump | InputButtons.Attack | InputButtons.Tongue | InputButtons.Strafe)
        );
        Check(
            simultaneous.Players[0].Mode == CharacterMode.Attacking && simultaneous.Players[0].VY > 0,
            "same-tick bat beats tongue and still permits jump, matching original input order"
        );
        var recorded = CreateWorld();
        var replay = InputReplay.Start(recorded);
        for (int tick = 0; tick < 160; tick++)
        {
            var inputs = new InputFrame[]
            {
                new(
                    (sbyte)(tick % 40 < 20 ? -1 : 1),
                    0,
                    InputButtons.Strafe | (tick % 60 < 15 ? InputButtons.Attack : 0)
                ),
                default,
            };
            recorded.Advance(inputs.Select(frame => new MatchInput(frame)).ToArray());
            replay.Record(inputs.Select(input => new MatchInput(input)).ToArray(), recorded);
            if (tick == 75)
            {
                var snapshot = recorded.Capture();
                recorded.Restore(snapshot);
                Check(
                    recorded.Players[0].PreviousInput.Strafe && recorded.Capture().SequenceEqual(snapshot),
                    "snapshot preserves strafe history and facing during charge"
                );
            }
        }

        var replayed = CreateWorld();
        replay.Play(replayed);
        Check(
            replayed.HashState() == recorded.HashState(),
            "strafe inputs replay deterministically from a canonical snapshot"
        );
    }

    public static void StrafingTongueAim()
    {
        (sbyte X, sbyte Y, int AimX, int AimY)[] directions =
        [
            (-1, 0, 1, 0),
            (0, 0, 1, 0),
            (1, 0, 1, 0),
            (-1, 1, 1, 1),
            (0, 1, 0, 1),
            (1, 1, 1, 1),
            (-1, -1, 1, -1),
            (0, -1, 0, -1),
            (1, -1, 1, -1),
        ];
        foreach (int facing in new[] { -1, 1 })
        foreach (
            var (mode, grounded) in new[]
            {
                (CharacterMode.Normal, true),
                (CharacterMode.Normal, false),
                (CharacterMode.Bouncing, false),
            }
        )
        foreach (var direction in directions)
        {
            var world = CreateWorld();
            var player = world.Players[0];
            player.Facing = facing;
            player.Mode = mode;
            player.OnGround = grounded;
            player.Y = grounded ? 0 : 10;
            player.VX = -facing * 5;
            player.CanBounceTongue = true;
            Step(world, new((sbyte)(direction.X * facing), direction.Y, InputButtons.Strafe | InputButtons.Tongue));
            var expected =
                grounded && direction.Y < 0
                    ? new FixedVector(facing, 0)
                    : new FixedVector(direction.AimX * facing, direction.AimY).Normalized;
            Check(
                player.Mode == CharacterMode.Tongue
                    && player.Facing == facing
                    && new FixedVector(player.TongueX, player.TongueY) == expected,
                $"strafing tongue keeps facing {facing}: {mode}, grounded {grounded}, input {direction.X},{direction.Y}"
            );
        }

        var aim = CreateWorld();
        Step(aim, new(-1, 0, InputButtons.Strafe | InputButtons.Tongue));
        Step(aim, new(-1, 1, InputButtons.Strafe), 8);
        Check(
            aim.Players[0].Facing == 1 && aim.Players[0].TongueX == 1 && aim.Players[0].TongueY == 0,
            "changing direction after launch does not turn or redirect a strafing tongue"
        );
        for (int tick = 0; tick < 120 && aim.Players[0].Mode == CharacterMode.Tongue; tick++)
            Step(aim, new(-1, 0, InputButtons.Strafe));
        Check(aim.Players[0].Mode == CharacterMode.Normal, "strafing tongue retracts normally");
        Step(aim, new(-1, 1, InputButtons.Tongue));
        var diagonal = new FixedVector(-1, 1).Normalized;
        Check(
            aim.Players[0].Mode == CharacterMode.Tongue
                && aim.Players[0].Facing == -1
                && new FixedVector(aim.Players[0].TongueX, aim.Players[0].TongueY) == diagonal,
            "releasing strafe restores directional tongue aim and turning on the next shot"
        );
    }

    public static void TongueCollisions()
    {
        MapData Wall(decimal x, decimal width, bool oneWay = false) =>
            new()
            {
                Id = "tongue-wall",
                Collision =
                [
                    new()
                    {
                        X = x,
                        Y = 5,
                        Width = width,
                        Height = 40,
                        OneWay = oneWay,
                    },
                    new()
                    {
                        X = 0,
                        Y = -1,
                        Width = 80,
                        Height = 2,
                    },
                ],
            };
        bool Latches(World world, InputFrame input, int ticks = 50)
        {
            bool latched = false;
            for (int i = 0; i < ticks; i++)
            {
                Step(world, i == 0 ? input : default);
                latched |= world.Events.Any(e => e.Kind == SimulationEventKind.TongueLatch);
            }

            return latched;
        }

        foreach (decimal width in new[] { .001m, .5m, 2m, 12m })
        {
            var world = CreateWorld(map: Wall(6 + width / 2, width));
            Check(
                Latches(world, new(0, 0, InputButtons.Tongue)),
                $"tongue attaches to distant solid wall of width {width}"
            );
        }

        foreach (int speed in new[] { -20, 0, 20 })
        {
            var world = CreateWorld(map: Wall(6, .02m));
            var p = world.Players[0];
            p.Y = 10;
            p.OnGround = false;
            p.VX = speed;
            Check(
                Latches(world, new(0, 0, InputButtons.Tongue)),
                $"tongue reaches thin terrain with initial horizontal velocity {speed}"
            );
        }

        var nearby = CreateWorld(map: Wall(2, .5m));
        Check(
            !Latches(nearby, new(0, 0, InputButtons.Tongue)),
            "original minimum range deliberately permits a thin wall entirely within three units to be passed through"
        );
        var fast = CreateWorld(map: Wall(6, .02m));
        fast.Players[0].Y = 10;
        fast.Players[0].OnGround = false;
        fast.Players[0].VX = 80;
        Check(
            !Latches(fast, new(0, 0, InputButtons.Tongue)) && fast.Players[0].X <= FromDecimal(4.99m),
            "fast grapple momentum reaches the wall during tongue delay, exposing the same original minimum-range pass-through"
        );
        var skip = CreateWorld(map: Wall(5.5m, .001m));
        var skipping = skip.Players[0];
        skipping.OnGround = false;
        skipping.Y = 10;
        skipping.VX = 240;
        skipping.Mode = CharacterMode.Tongue;
        skipping.TongueDistance = 4;
        skipping.TonguePhase = TonguePhase.Extending;
        Step(skip);
        Check(
            skipping.TongueTip.X > 6 && skipping.TonguePhase == TonguePhase.Extending,
            "extreme knockback can skip a thin wall between endpoint queries, as in the original unswept tongue overlap"
        );
        var platformSide = CreateWorld(map: Wall(6, .5m, true));
        Check(
            !Latches(platformSide, new(0, 0, InputButtons.Tongue)),
            "horizontal tongue ignores one-way terrain like the source layer mask"
        );
        foreach (int direction in new[] { -1, 1 })
        {
            var map = new MapData
            {
                Id = "tongue-ceiling",
                Collision =
                [
                    new()
                    {
                        X = 0,
                        Y = direction < 0 ? -4 : 7,
                        Width = 50,
                        Height = .02m,
                        OneWay = true,
                    },
                ],
            };
            var world = CreateWorld(map: map);
            world.Players[0].OnGround = false;
            Check(
                Latches(world, new(0, (sbyte)direction, InputButtons.Tongue)) == (direction < 0),
                "only downward tongue queries include one-way platforms"
            );
        }

        var diagonal = CreateWorld(
            map: new()
            {
                Id = "tongue-diagonal",
                Collision =
                [
                    new()
                    {
                        X = 5,
                        Y = 5,
                        Width = .05m,
                        Height = 20,
                    },
                ],
            }
        );
        diagonal.Players[0].OnGround = false;
        Check(Latches(diagonal, new(1, 1, InputButtons.Tongue)), "diagonal tongue attaches to a thin vertical solid");
        var checkpoint = CreateWorld(map: Wall(6, .1m));
        Step(checkpoint, new(0, 0, InputButtons.Tongue), 12);
        var bytes = checkpoint.Capture();
        var events = new List<SimulationEvent>();
        for (int i = 0; i < 45; i++)
        {
            Step(checkpoint);
            events.AddRange(checkpoint.Events);
        }

        ulong expected = checkpoint.HashState();
        checkpoint.Restore(bytes);
        var after = new List<SimulationEvent>();
        for (int i = 0; i < 45; i++)
        {
            Step(checkpoint);
            after.AddRange(checkpoint.Events);
        }

        Check(
            checkpoint.HashState() == expected && events.SequenceEqual(after),
            "tongue collision, attachment and effect metadata restore exactly across rollback"
        );
    }
}
