using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    public const int TickRate = 100;
    public static readonly Fixed TickDuration = (Fixed)1 / TickRate;

    public static int TicksFromSeconds(decimal seconds) =>
        checked((int)decimal.Round(seconds * TickRate, MidpointRounding.AwayFromZero));

    private readonly IReadOnlyList<MapData> maps;
    private readonly CollisionMap[] collisionMaps;
    private readonly CharacterTuning tuning;
    private readonly List<SimulationEvent> events = new();
    private uint randomState;
    public GameRules Rules { get; }
    public MatchState Match { get; private set; }
    public PlayerState[] Players { get; private set; }
    public FlyState Fly { get; private set; } = new();
    public MapData Map => maps[Match.CurrentMapIndex];
    public long TickNumber { get; private set; }
    public ulong ConfigurationHash { get; private set; }
    public IReadOnlyList<SimulationEvent> Events => events;

    public World(MapData map, GameRules rules, uint seed = 1)
        : this([map], rules, seed, null) { }

    public World(GameContent content, GameRules rules, uint seed = 1)
        : this(content.Maps, rules, seed, content.CharacterParameters) { }

    public World(
        IReadOnlyList<MapData> availableMaps,
        GameRules rules,
        uint seed,
        Dictionary<string, decimal>? characterParameters
    )
    {
        rules.ValidateWorld(availableMaps);
        Rules = rules;
        Match = new MatchState(rules);
        maps = availableMaps;
        collisionMaps = availableMaps.Select(map => new CollisionMap(map)).ToArray();
        tuning = new CharacterTuning(characterParameters);
        randomState = seed == 0 ? 1u : seed;
        Players = Enumerable
            .Range(0, rules.PlayerCount)
            .Select(i => new PlayerState
            {
                Slot = i,
                Team = rules.Teams[i],
                ColorIndex = rules.Colors[i],
            })
            .ToArray();
        Match.CurrentMapIndex = ChooseMap(0);
        ConfigurationHash = ComputeConfigurationHash();
        StartRound();
    }

    private uint RandomUInt()
    {
        uint x = randomState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return randomState = x;
    }

    private int RandomRange(int min, int max) => min + (int)(RandomUInt() % (uint)(max - min));

    private void Emit(
        SimulationEventKind kind,
        PlayerState? player = null,
        int other = -1,
        Fixed strength = default,
        Fixed power = default,
        int comboHits = 0,
        HitKind hitKind = HitKind.Bat,
        FixedVector velocity = default,
        FixedVector? position = null,
        int surfaceSide = 0,
        Fixed hitstopSeconds = default,
        int scoreDelta = 0
    ) =>
        events.Add(
            new(
                TickNumber,
                events.Count,
                kind,
                player?.Slot ?? -1,
                other,
                position?.X ?? player?.X ?? Fly.X,
                position?.Y ?? player?.Y ?? Fly.Y,
                strength,
                power,
                comboHits,
                hitKind,
                velocity.X,
                velocity.Y,
                surfaceSide,
                hitstopSeconds,
                player?.Center.X ?? Fly.X,
                player?.Center.Y ?? Fly.Y,
                scoreDelta
            )
        );

    public void Advance(MatchInput[] inputs)
    {
        if (inputs.Length != Players.Length)
        {
            throw new ArgumentException("Supply one input per player", nameof(inputs));
        }

        foreach (var input in inputs)
        {
            input.Validate();
        }

        events.Clear();
        for (int slot = 0; slot < inputs.Length; slot++)
            Match.ApplySelection(Rules, slot, inputs[slot].Command);
        if (Match.Phase == MatchPhase.Selecting)
        {
            TickNumber++;
            return;
        }
        if (Match.Phase == MatchPhase.MatchFinished)
        {
            TickNumber++;
            return;
        }

        if (Match.Phase == MatchPhase.RoundScores)
        {
            if (--Match.PhaseTicks <= 0)
            {
                AdvanceRound();
            }

            TickNumber++;
            return;
        }

        if (!Rules.Lobby && Rules.CpuPlayers.Any(cpu => cpu))
        {
            inputs = inputs.ToArray();
            for (int slot = 0; slot < Players.Length; slot++)
                if (Rules.CpuPlayers[slot])
                    inputs[slot] = new(BotController.GetInput(this, slot));
        }
        for (int slot = 0; slot < Players.Length; slot++)
        {
            TickPlayer(Players[slot], inputs[slot].Gameplay);
        }

        if (!Rules.Lobby)
        {
            UpdateFly();
        }
        if (Match.Phase == MatchPhase.RoundFinished && --Match.PhaseTicks <= 0)
        {
            if (Rules.ScoreScreenTicks > 0)
            {
                Match.Phase = MatchPhase.RoundScores;
                Match.PhaseTicks = Rules.ScoreScreenTicks;
            }
            else
            {
                AdvanceRound();
            }
        }

        TickNumber++;
    }

    private void TickPlayer(PlayerState player, InputFrame input)
    {
        if (Match.Phase == MatchPhase.RoundFinished && !IsWinner(player))
        {
            input = default;
        }

        if (!player.Alive)
        {
            if (
                Match.Players[player.Slot].Participation == Participation.Active
                && (Match.Phase == MatchPhase.Playing || IsWinner(player))
                && --player.SpawnTicks <= 0
            )
            {
                Spawn(player);
            }

            player.PreviousInput = input;
            return;
        }

        bool endingLaunchFreeze =
            player.Mode == CharacterMode.Bouncing && player.HitstopTicks == 1 && player.HitstopScale == 0;
        player.LocalDelta = TickDuration;
        if (player.HitstopTicks > 0)
        {
            player.HitstopTicks--;
            player.LocalDelta *= player.HitstopScale;
        }
        else
        {
            player.HitstopScale = 1;
        }

        var priorMode = player.Mode;
        if (player.Mode == CharacterMode.Bouncing)
        {
            UpdateBounceInput(player, input);
        }
        else
        {
            UpdateNormalInput(player, input);
        }

        var priorTongueOrigin = player.TongueOrigin;
        UpdateVelocity(player, input);
        BounceOffPlayers(player);
        MoveAndCollide(player, input);
        if (player.Mode == CharacterMode.Attacking)
        {
            UpdateAttack(player, input);
        }
        else if (player.Mode == CharacterMode.Tongue)
        {
            UpdateTongue(player, priorTongueOrigin);
        }

        if (
            player.X < FromDecimal(Map.KillBounds.Left)
            || player.X > FromDecimal(Map.KillBounds.Right)
            || player.Y < FromDecimal(Map.KillBounds.Bottom)
            || player.Y > FromDecimal(Map.KillBounds.Top)
        )
        {
            Kill(player);
        }

        if (priorMode != player.Mode)
        {
            player.StateStartTick = TickNumber;
        }

        UpdateAnimation(player);
        if (
            endingLaunchFreeze
            && player.Alive
            && player.Mode == CharacterMode.Bouncing
            && !player.OnGround
            && !player.HasBounceDodged
            && player.HitsTaken >= 5
            && player.HitstopTicks == 0
        )
        {
            Emit(SimulationEventKind.Launch, player, comboHits: player.HitsTaken, velocity: player.Velocity);
        }

        player.PreviousInput = input;
    }
}
