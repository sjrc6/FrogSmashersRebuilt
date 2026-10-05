using FrogSmashers.Core;

namespace FrogSmashers.Network;

[Flags]
public enum LobbyInputActions : byte
{
    None = 0,
    Spawn = 1,
    SelectColor = 2,
}

public sealed class LobbySimulation : IRollbackSimulation
{
    private const int SnapshotMagic = 0x4c465352;
    private LobbyBots bots = new();
    private uint randomState;
    private uint pendingPreviews;
    private readonly List<SimulationEvent> events = new();
    private int[] inputRooms = [];
    private LobbyInputSource[] inputSources = [];
    public int CpuRevision { get; private set; }
    public int RosterRevision { get; private set; } = 1;
    public World World { get; }
    public LobbyMembership Membership { get; private set; } = new(new LobbyPlayer?[LobbyRoster.MaxPlayers], []);
    public IReadOnlyList<SimulationEvent> Events => events;
    public IReadOnlyList<int> InputRooms => Array.AsReadOnly(inputRooms);
    public IReadOnlyList<LobbyInputSource> InputSources => Array.AsReadOnly(inputSources);

    public LobbySimulation(World world, LobbyRoster roster, uint colorSeed = 1)
    {
        if (!world.Rules.Lobby || world.Players.Length != LobbyRoster.MaxPlayers)
            throw new ArgumentException("Lobby simulation requires an eight-room lobby world", nameof(world));
        World = world;
        randomState = colorSeed == 0 ? 1u : colorSeed;
        ApplyRoster(roster);
    }

    public int RoomForHandle(int handle) => inputRooms[handle];

    public static bool OnStartingPlatform(World world, int room)
    {
        var player = world.Players[room];
        var spawn = world.Map.Spawns[room];
        if (!player.Alive || !player.OnGround || player.Y != Fixed.FromDecimal(spawn.Y))
            return false;
        return world.Map.Collision.Any(platform =>
            platform.X == spawn.X
            && platform.Y + platform.Height / 2 == spawn.Y
            && player.X >= Fixed.FromDecimal(platform.X - platform.Width / 2)
            && player.X <= Fixed.FromDecimal(platform.X + platform.Width / 2)
        );
    }

    public void ApplyRoster(LobbyRoster requested)
    {
        var next = requested.Slots.Select(slot => slot.Player).ToArray();
        var oldRooms = Enumerable.Repeat(-1, LobbyRoster.MaxPlayers).ToArray();
        var oldBodies = World.Players.ToArray();
        var oldProgress = World.Match.Players.ToArray();
        uint previousPreviews = pendingPreviews;
        var usedColors = new HashSet<int>();
        var assigned = new List<LobbyPlayer>();
        for (int room = 0; room < next.Length; room++)
        {
            if (next[room] is not { } incoming)
                continue;
            int previous = FindRoom(incoming.Peer, incoming.Id);
            if (previous < 0)
                continue;
            var old = Membership.Rooms[previous]!;
            oldRooms[room] = previous;
            next[room] = incoming with { Color = old.Color, Team = old.Team, Spawned = old.Spawned };
            usedColors.Add(old.Color);
            assigned.Add(next[room]!);
        }
        for (int room = 0; room < next.Length; room++)
        {
            if (oldRooms[room] >= 0 || next[room] is not { } incoming)
                continue;
            int color = usedColors.Contains(incoming.Color)
                ? Enumerable.Range(0, LobbyRoster.MaxPlayers).First(value => !usedColors.Contains(value))
                : incoming.Color;
            next[room] = incoming with { Color = color, Team = LobbyRoster.AvailableTeam(incoming.Team, assigned) };
            usedColors.Add(color);
            assigned.Add(next[room]!);
        }
        var spectators = requested
            .Spectators.Select(spectator =>
            {
                int previous = FindRoom(spectator.Peer, spectator.Id);
                return previous < 0
                    ? spectator
                    : spectator with
                    {
                        Color = Membership.Rooms[previous]!.Color,
                        Team = Membership.Rooms[previous]!.Team,
                    };
            })
            .ToArray();
        var membership = new LobbyMembership(next, spectators);
        for (int room = 0; room < next.Length; room++)
            World.SetLobbySlot(room, false, next[room]?.Color ?? room);
        for (int room = 0; room < next.Length; room++)
        {
            if (next[room] is not { } player)
                continue;
            if (oldRooms[room] is int previous && previous >= 0)
            {
                World.Players[room] = oldBodies[previous];
                World.Match.Players[room] = oldProgress[previous];
                World.Players[room].Slot = room;
                World.Players[room].Team = World.Rules.Teams[room];
                int lastAttacker = World.Players[room].LastHitBy;
                World.Players[room].LastHitBy = lastAttacker < 0 ? -1 : Array.IndexOf(oldRooms, lastAttacker);
            }
            World.SetLobbySlot(room, player.Spawned, player.Color);
        }
        int ballAttacker = World.BeachBall.LastHitBy;
        World.BeachBall.LastHitBy = ballAttacker < 0 ? -1 : Array.IndexOf(oldRooms, ballAttacker);
        bots.RemapRooms(oldRooms);
        pendingPreviews = 0;
        for (int room = 0; room < next.Length; room++)
            if (
                next[room] is { Spawned: false }
                && (oldRooms[room] < 0 || (previousPreviews & (1u << oldRooms[room])) != 0)
            )
                pendingPreviews |= 1u << room;
        Membership = membership;
        RebuildInputs();
    }

    public void Tick(ReadOnlySpan<RollbackInput> inputs)
    {
        if (inputs.Length != inputSources.Length)
            throw new ArgumentException("Supply the host command stream followed by human inputs", nameof(inputs));
        for (int handle = 0; handle < inputs.Length; handle++)
        {
            var input = inputs[handle];
            _ = InputFrame.FromPacked(input.Gameplay.Packed);
            if (
                (input.Actions & ~(byte)(LobbyInputActions.Spawn | LobbyInputActions.SelectColor)) != 0
                || input.ColorStep is < -1 or > 1
                || input.TeamStep is < -1 or > 1
                || !input.Cpu.IsValid
                || handle != 0 && input.Cpu != default
                || handle == 0
                    && (input.Gameplay != default || input.Actions != 0 || input.ColorStep != 0 || input.TeamStep != 0)
            )
                throw new ArgumentException("Invalid lobby command", nameof(inputs));
        }
        ApplyCpuCommand(inputs[0].Cpu);
        var gameplay = new MatchInput[LobbyRoster.MaxPlayers];
        var players = Membership.Rooms.ToArray();
        bool changed = false;
        for (int handle = 1; handle < inputRooms.Length; handle++)
        {
            int room = inputRooms[handle];
            var player = players[room]!;
            var input = inputs[handle];
            var actions = (LobbyInputActions)input.Actions;
            if ((actions & LobbyInputActions.SelectColor) != 0 && player.Spawned && OnStartingPlatform(World, room))
                player = player with { Spawned = false };
            if (!player.Spawned)
            {
                if (input.ColorStep != 0)
                {
                    var colors = players.Select(player => player?.Color).ToHashSet();
                    int[] available = Enumerable
                        .Range(0, LobbyRoster.MaxPlayers)
                        .Where(color => !colors.Contains(color))
                        .ToArray();
                    if (available.Length > 0)
                        player = player with { Color = available[RandomIndex(available.Length)] };
                }
                if (input.TeamStep != 0)
                    player = player with
                    {
                        Team = LobbyRoster.AvailableTeam(
                            (player.Team + input.TeamStep + LobbyRoster.MaxPlayers) % LobbyRoster.MaxPlayers,
                            players.Where((_, index) => index != room),
                            input.TeamStep
                        ),
                    };
                if ((actions & LobbyInputActions.Spawn) != 0)
                    player = player with { Spawned = true };
            }
            if (player != players[room])
            {
                if (player.Spawned != players[room]!.Spawned)
                    bots.ResetRoom(room);
                players[room] = player;
                if (!player.Spawned)
                    pendingPreviews |= 1u << room;
                changed = true;
            }
            World.SetLobbySlot(room, player.Spawned, player.Color);
            if (player.Spawned)
                gameplay[room] = new(input.Gameplay);
        }
        if (changed)
        {
            Membership = new LobbyMembership(players, Membership.Spectators);
            RebuildInputs();
        }
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
            if (Membership.Rooms[room] is { Cpu: true, Spawned: true } cpu)
            {
                World.SetLobbySlot(room, true, cpu.Color);
                gameplay[room] = new(bots.Read(World, room));
            }
        World.Advance(gameplay);
        bots.Observe(World, Membership);
        events.Clear();
        events.AddRange(World.Events);
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
        {
            if ((pendingPreviews & (1u << room)) == 0 || Membership.Rooms[room] is not { Spawned: false } player)
                continue;
            var spawn = World.Map.Spawns[room];
            events.Add(
                new(
                    World.TickNumber - 1,
                    events.Count,
                    SimulationEventKind.LobbyPreview,
                    room,
                    player.Color,
                    Fixed.FromDecimal(spawn.X),
                    Fixed.FromDecimal(spawn.Y),
                    default
                )
            );
        }
        pendingPreviews = 0;
    }

    internal void TickFrame(ReadOnlySpan<GGCS.PlayerInput<LobbyFrame>> inputs)
    {
        var host = inputs[0].Input;
        if (host.Membership.Revision > RosterRevision)
        {
            var next = host.Membership.Membership(Membership);
            var roster = new LobbyRoster();
            var slots = next
                .Rooms.Select(
                    (player, room) =>
                    {
                        var occupant =
                            player is { Cpu: false } ? player
                            : Membership.Rooms[room] is { Cpu: true } cpu ? cpu
                            : null;
                        return new LobbySlot(occupant is { Cpu: true } ? SlotType.Cpu : SlotType.Open, occupant);
                    }
                )
                .ToArray();
            roster.Replace(slots, next.Spectators);
            ApplyRoster(roster);
            RosterRevision = host.Membership.Revision;
        }
        var controls = new RollbackInput[inputSources.Length];
        controls[0] = new(default, Cpu: host.Cpu);
        for (int handle = 1; handle < controls.Length; handle++)
        {
            var player = inputSources[handle];
            var frame = inputs[player.Peer];
            if (frame.Status != GGCS.InputStatus.Disconnected)
                controls[handle] = frame.Input.Controls[player.Id];
        }
        Tick(controls);
    }

    public byte[] Capture() => Capture(out _);

    public byte[] Capture(out byte[] worldSnapshot)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(SnapshotMagic);
        writer.Write(RosterRevision);
        writer.Write(CpuRevision);
        writer.Write(randomState);
        writer.Write(pendingPreviews);
        worldSnapshot = World.Capture();
        writer.Write(worldSnapshot.Length);
        writer.Write(worldSnapshot);
        foreach (var player in Membership.Rooms)
        {
            writer.Write(player != null);
            player?.WriteSnapshot(writer);
        }
        writer.Write((byte)Membership.Spectators.Count);
        foreach (var spectator in Membership.Spectators)
            spectator.WriteSnapshot(writer);
        bots.Write(writer);
        return stream.ToArray();
    }

    public void Restore(byte[] snapshot) => Restore(snapshot, out _);

    public void Restore(byte[] snapshot, out byte[] worldSnapshot)
    {
        if (snapshot.Length > 128 * 1024)
            throw new InvalidDataException("Oversized lobby snapshot");
        using var stream = new MemoryStream(snapshot, false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != SnapshotMagic)
            throw new InvalidDataException("Invalid lobby snapshot");
        int rosterRevision = reader.ReadInt32();
        int cpuRevision = reader.ReadInt32();
        uint nextRandom = reader.ReadUInt32();
        uint nextPreviews = reader.ReadUInt32();
        int worldLength = reader.ReadInt32();
        if (
            rosterRevision < 1
            || cpuRevision < 0
            || nextRandom == 0
            || nextPreviews >= 1u << LobbyRoster.MaxPlayers
            || worldLength < 0
            || worldLength > 64 * 1024
            || worldLength > stream.Length - stream.Position
        )
            throw new InvalidDataException("Invalid lobby snapshot state");
        byte[] world = reader.ReadBytes(worldLength);
        var players = new LobbyPlayer?[LobbyRoster.MaxPlayers];
        for (int room = 0; room < players.Length; room++)
            players[room] = reader.ReadBoolean() ? LobbyPlayer.ReadSnapshot(reader) : null;
        int spectatorCount = reader.ReadByte();
        if (spectatorCount > LobbyRoster.MaxSpectators)
            throw new InvalidDataException("Invalid lobby spectators");
        var spectators = new LobbyPlayer[spectatorCount];
        for (int index = 0; index < spectators.Length; index++)
            spectators[index] = LobbyPlayer.ReadSnapshot(reader);
        var nextBots = LobbyBots.Read(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Invalid lobby snapshot payload");
        var membership = new LobbyMembership(players, spectators);
        World.Restore(world);
        worldSnapshot = world;
        bots = nextBots;
        CpuRevision = cpuRevision;
        RosterRevision = rosterRevision;
        randomState = nextRandom;
        pendingPreviews = nextPreviews;
        Membership = membership;
        events.Clear();
        RebuildInputs();
    }

    private int FindRoom(int peer, int id)
    {
        for (int room = 0; room < Membership.Rooms.Count; room++)
            if (Membership.Rooms[room] is { } player && player.Peer == peer && player.Id == id)
                return room;
        return -1;
    }

    private void RebuildInputs()
    {
        inputSources =
        [
            new(0, -1, -1),
            .. Membership
                .Rooms.Select((player, room) => (player, room))
                .Where(item => item.player is { Cpu: false })
                .Select(item => new LobbyInputSource(item.player!.Peer, item.player.Id, item.room)),
        ];
        inputRooms = inputSources.Select(source => source.Room).ToArray();
    }

    internal static int ReadCpuRevision(byte[] snapshot)
    {
        using var reader = new BinaryReader(new MemoryStream(snapshot, false));
        if (reader.ReadInt32() != SnapshotMagic)
            throw new InvalidDataException("Invalid lobby snapshot");
        reader.ReadInt32();
        return reader.ReadInt32();
    }

    internal static int ReadRosterRevision(byte[] snapshot) => BitConverter.ToInt32(snapshot, 4);

    public void ApplyCpuCommand(LobbyCpuCommand command)
    {
        if (command.Revision <= CpuRevision)
            return;
        var players = Membership.Rooms.ToArray();
        for (int room = 0; room < players.Length; room++)
        {
            if ((command.Rooms & (1 << room)) == 0 || players[room] is { Cpu: false })
                continue;
            if ((command.Enabled & (1 << room)) == 0)
            {
                players[room] = null;
                World.SetLobbySlot(room, false, room);
                foreach (var body in World.Players)
                    if (body.LastHitBy == room)
                        body.LastHitBy = -1;
                if (World.BeachBall.LastHitBy == room)
                    World.BeachBall.LastHitBy = -1;
                bots.ResetRoom(room);
                pendingPreviews &= ~(1u << room);
            }
        }
        for (int room = 0; room < players.Length; room++)
        {
            if ((command.Enabled & (1 << room)) == 0 || players[room] is { Cpu: false })
                continue;
            bool created = players[room] == null;
            var used = players.Where((_, index) => index != room).Select(player => player?.Color).ToHashSet();
            int color = command.Color(room);
            if (used.Contains(color))
                color = Enumerable.Range(0, LobbyRoster.MaxPlayers).First(value => !used.Contains(value));
            int team = LobbyRoster.AvailableTeam(command.Team(room), players.Where((_, index) => index != room));
            players[room] = new(10 + room, Team: team, Color: color, Spawned: true, Cpu: true);
            if (created)
            {
                World.SetLobbySlot(room, false, color);
                if (World.BeachBall.LastHitBy == room)
                    World.BeachBall.LastHitBy = -1;
                bots.ResetRoom(room);
                pendingPreviews &= ~(1u << room);
            }
            World.SetLobbySlot(room, true, color);
        }
        Membership = new LobbyMembership(players, Membership.Spectators);
        CpuRevision = command.Revision;
    }

    private int RandomIndex(int length)
    {
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        return (int)(randomState % (uint)length);
    }
}
