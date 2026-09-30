using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    public const int TickRate = 120;
    public static readonly Fixed TickDuration = (Fixed)1 / TickRate;
    private readonly IReadOnlyList<MapData> maps;
    private readonly CollisionMap[] collisionMaps;
    private readonly CharacterTuning tuning;
    private readonly List<SimulationEvent> events = new();
    private uint randomState;
    public GameRules Rules { get; }
    public PlayerState[] Players { get; private set; }
    public FlyState Fly { get; private set; } = new();
    public MapData Map => maps[CurrentMapIndex];
    public int CurrentMapIndex { get; private set; }
    public long TickNumber { get; private set; }
    public int RoundNumber { get; private set; } = 1;
    public MatchPhase Phase { get; private set; }
    public int Winner { get; private set; } = -1;
    public int PhaseTicks { get; private set; }
    public bool IsShowdown { get; private set; }
    public ulong ConfigurationHash { get; private set; }
    public IReadOnlyList<SimulationEvent> Events => events;
    public int TargetScore =>
        Rules.WinScore > 0 ? Rules.WinScore
        : !Rules.TeamMode && Players.Length == 2 ? 5
        : 10;

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
        if (rules.PlayerCount is < 2 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(rules.PlayerCount));
        }

        if (rules.Colors.Length != 8 || rules.Colors.Any(color => color is < 0 or > 7))
        {
            throw new ArgumentException("Invalid player colors");
        }

        if (availableMaps.Count == 0)
        {
            throw new ArgumentException("A world needs at least one map");
        }

        if (rules.Lobby && availableMaps.Any(map => map.Spawns.Count < rules.PlayerCount))
            throw new ArgumentException("A lobby needs a spawn point for every room");

        if (rules.Teams.Length < rules.PlayerCount || rules.Teams.Any(team => team < 0 || team > 7))
        {
            throw new ArgumentException("Invalid teams");
        }

        if (rules.MatchRounds < 1 || rules.WinScore < 0 || rules.RoundFinishTicks < 0 || rules.ScoreScreenTicks < 0)
        {
            throw new ArgumentException("Invalid match rules");
        }

        if (rules.MapOrder.Any(i => i < 0 || i >= availableMaps.Count))
        {
            throw new ArgumentException("Invalid map order");
        }

        Rules = new GameRules
        {
            PlayerCount = rules.PlayerCount,
            Lobby = rules.Lobby,
            Colors = rules.Colors.ToArray(),
            TeamMode = rules.TeamMode,
            Teams = (int[])rules.Teams.Clone(),
            WinScore = rules.WinScore,
            MatchRounds = rules.MatchRounds,
            CharactersBounceEachOther = rules.CharactersBounceEachOther,
            Showdown = rules.Showdown,
            RoundFinishTicks = rules.RoundFinishTicks,
            ScoreScreenTicks = rules.ScoreScreenTicks,
            MapOrder = (int[])rules.MapOrder.Clone(),
        };
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
                Eliminated = rules.Lobby,
            })
            .ToArray();
        IsShowdown = rules.Showdown;
        CurrentMapIndex = ChooseMap(0);
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
        FixedVector? hitEffectPosition = null,
        bool awardedScore = false
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
                hitEffectPosition?.X ?? player?.Center.X ?? Fly.X,
                hitEffectPosition?.Y ?? player?.Center.Y ?? Fly.Y,
                awardedScore
            )
        );

    public void Tick(InputFrame[] inputs)
    {
        if (inputs.Length != Players.Length)
        {
            throw new ArgumentException("Supply one input per player", nameof(inputs));
        }

        foreach (var input in inputs)
        {
            _ = InputFrame.FromPacked(input.Packed);
        }

        events.Clear();
        if (Phase == MatchPhase.MatchFinished)
        {
            TickNumber++;
            return;
        }

        if (Phase == MatchPhase.RoundScores)
        {
            if (--PhaseTicks <= 0)
            {
                AdvanceRound();
            }

            TickNumber++;
            return;
        }

        for (int slot = 0; slot < Players.Length; slot++)
        {
            TickPlayer(Players[slot], inputs[slot]);
        }

        if (!Rules.Lobby)
        {
            UpdateFly();
        }
        if (Phase == MatchPhase.RoundFinished && --PhaseTicks <= 0)
        {
            if (Rules.ScoreScreenTicks > 0)
            {
                Phase = MatchPhase.RoundScores;
                PhaseTicks = Rules.ScoreScreenTicks;
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
        if (Phase == MatchPhase.RoundFinished && !IsWinner(player))
        {
            input = default;
        }

        if (!player.Alive)
        {
            if (!player.Eliminated && (Phase == MatchPhase.Playing || IsWinner(player)) && --player.SpawnTicks <= 0)
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

        UpdateVelocity(player, input);
        MoveAndCollide(player, input);
        if (player.Mode == CharacterMode.Attacking)
        {
            UpdateAttack(player, input);
        }
        else if (player.Mode == CharacterMode.Tongue)
        {
            UpdateTongue(player, input);
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
