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
    private LobbyPlayer[] inputPlayers = [];
    public World World { get; }
    public LobbyRoster Roster { get; private set; } = new();
    public IReadOnlyList<SimulationEvent> Events => events;
    public IReadOnlyList<int> InputRooms => Array.AsReadOnly(inputRooms);
    public IReadOnlyList<LobbyPlayer> InputPlayers => Array.AsReadOnly(inputPlayers);

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
        var next = requested.Slots.ToArray();
        var oldRooms = Enumerable.Repeat(-1, LobbyRoster.MaxPlayers).ToArray();
        var oldBodies = World.Players.ToArray();
        uint previousPreviews = pendingPreviews;
        var usedColors = new HashSet<int>();
        for (int room = 0; room < next.Length; room++)
        {
            if (next[room].Player is not { } incoming)
                continue;
            int previous = FindRoom(incoming.Peer, incoming.Id);
            if (previous < 0)
                continue;
            var old = Roster.Slots[previous].Player!;
            oldRooms[room] = previous;
            next[room] = next[room] with
            {
                Player = incoming with { Color = old.Color, Team = old.Team, Spawned = old.Spawned },
            };
            usedColors.Add(old.Color);
        }
        for (int room = 0; room < next.Length; room++)
        {
            if (oldRooms[room] >= 0 || next[room].Player is not { } incoming)
                continue;
            int color = usedColors.Contains(incoming.Color)
                ? Enumerable.Range(0, LobbyRoster.MaxPlayers).First(value => !usedColors.Contains(value))
                : incoming.Color;
            next[room] = next[room] with { Player = incoming with { Color = color } };
            usedColors.Add(color);
        }
        var roster = new LobbyRoster();
        var spectators = requested
            .Spectators.Select(spectator =>
            {
                int previous = FindRoom(spectator.Peer, spectator.Id);
                return previous < 0
                    ? spectator
                    : spectator with
                    {
                        Color = Roster.Slots[previous].Player!.Color,
                        Team = Roster.Slots[previous].Player!.Team,
                    };
            })
            .ToArray();
        roster.Replace(next, spectators);
        for (int room = 0; room < next.Length; room++)
            World.SetLobbySlot(room, false, next[room].Player?.Color ?? room);
        for (int room = 0; room < next.Length; room++)
        {
            if (next[room].Player is not { } player)
                continue;
            if (oldRooms[room] is int previous && previous >= 0)
            {
                World.Players[room] = oldBodies[previous];
                World.Players[room].Slot = room;
                World.Players[room].Team = World.Rules.Teams[room];
                int lastAttacker = World.Players[room].LastHitBy;
                World.Players[room].LastHitBy = lastAttacker < 0 ? -1 : Array.IndexOf(oldRooms, lastAttacker);
            }
            World.SetLobbySlot(room, player.Spawned, player.Color);
        }
        bots.RemapRooms(oldRooms);
        pendingPreviews = 0;
        for (int room = 0; room < next.Length; room++)
            if (
                next[room].Player is { Spawned: false }
                && (oldRooms[room] < 0 || (previousPreviews & (1u << oldRooms[room])) != 0)
            )
                pendingPreviews |= 1u << room;
        Roster = roster;
        RebuildInputs();
    }

    public void Tick(ReadOnlySpan<RollbackInput> inputs)
    {
        if (inputs.Length != inputRooms.Length && !(inputRooms.Length == 0 && inputs.Length == 1))
            throw new ArgumentException("Supply one input per occupied lobby room", nameof(inputs));
        foreach (var input in inputs)
        {
            _ = InputFrame.FromPacked(input.Gameplay.Packed);
            if (
                (input.Actions & ~(byte)(LobbyInputActions.Spawn | LobbyInputActions.SelectColor)) != 0
                || input.ColorStep is < -1 or > 1
                || input.TeamStep is < -1 or > 1
            )
                throw new ArgumentException("Invalid lobby command", nameof(inputs));
        }
        var gameplay = new InputFrame[LobbyRoster.MaxPlayers];
        var slots = Roster.Slots.ToArray();
        bool changed = false;
        for (int handle = 0; handle < inputRooms.Length; handle++)
        {
            int room = inputRooms[handle];
            var player = slots[room].Player!;
            var input = inputs[handle];
            var actions = (LobbyInputActions)input.Actions;
            if (
                (actions & LobbyInputActions.SelectColor) != 0
                && player.Spawned
                && !player.Cpu
                && OnStartingPlatform(World, room)
            )
                player = player with { Spawned = false };
            if (!player.Spawned || player.Cpu)
            {
                if (input.ColorStep != 0)
                {
                    var colors = slots.Select(slot => slot.Player?.Color).ToHashSet();
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
                        Team = (player.Team + input.TeamStep + LobbyRoster.MaxPlayers) % LobbyRoster.MaxPlayers,
                    };
                if ((actions & LobbyInputActions.Spawn) != 0)
                    player = player with { Spawned = true };
            }
            if (player != slots[room].Player)
            {
                if (player.Spawned != slots[room].Player!.Spawned)
                    bots.ResetRoom(room);
                slots[room] = slots[room] with { Player = player };
                if (!player.Spawned)
                    pendingPreviews |= 1u << room;
                changed = true;
            }
            World.SetLobbySlot(room, player.Spawned, player.Color);
            if (player.Spawned)
                gameplay[room] = player.Cpu ? bots.Read(World, room) : input.Gameplay;
        }
        if (changed)
        {
            Roster.Replace(slots, Roster.Spectators);
            RebuildInputs();
        }
        World.Tick(gameplay);
        bots.Observe(World, Roster);
        events.Clear();
        events.AddRange(World.Events);
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
        {
            if ((pendingPreviews & (1u << room)) == 0 || Roster.Slots[room].Player is not { Spawned: false } player)
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

    public byte[] Capture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(SnapshotMagic);
        writer.Write(randomState);
        writer.Write(pendingPreviews);
        byte[] world = World.Capture();
        writer.Write(world.Length);
        writer.Write(world);
        foreach (var slot in Roster.Slots)
        {
            writer.Write((byte)slot.Type);
            writer.Write(slot.Player != null);
            if (slot.Player is { } player)
                WritePlayer(writer, player);
        }
        writer.Write((byte)Roster.Spectators.Count);
        foreach (var spectator in Roster.Spectators)
            WritePlayer(writer, spectator);
        bots.Write(writer);
        return stream.ToArray();
    }

    public void Restore(byte[] snapshot)
    {
        if (snapshot.Length > 128 * 1024)
            throw new InvalidDataException("Oversized lobby snapshot");
        using var stream = new MemoryStream(snapshot, false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != SnapshotMagic)
            throw new InvalidDataException("Invalid lobby snapshot");
        uint nextRandom = reader.ReadUInt32();
        uint nextPreviews = reader.ReadUInt32();
        int worldLength = reader.ReadInt32();
        if (
            nextRandom == 0
            || nextPreviews >= 1u << LobbyRoster.MaxPlayers
            || worldLength < 0
            || worldLength > 64 * 1024
            || worldLength > stream.Length - stream.Position
        )
            throw new InvalidDataException("Invalid lobby snapshot state");
        byte[] world = reader.ReadBytes(worldLength);
        var slots = new LobbySlot[LobbyRoster.MaxPlayers];
        for (int room = 0; room < slots.Length; room++)
            slots[room] = new((SlotType)reader.ReadByte(), reader.ReadBoolean() ? ReadPlayer(reader) : null);
        int spectatorCount = reader.ReadByte();
        if (spectatorCount > LobbyRoster.MaxSpectators)
            throw new InvalidDataException("Invalid lobby spectators");
        var spectators = new LobbyPlayer[spectatorCount];
        for (int index = 0; index < spectators.Length; index++)
            spectators[index] = ReadPlayer(reader);
        var nextBots = LobbyBots.Read(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Invalid lobby snapshot payload");
        var nextRoster = new LobbyRoster();
        nextRoster.Replace(slots, spectators);
        World.Restore(world);
        bots = nextBots;
        randomState = nextRandom;
        pendingPreviews = nextPreviews;
        Roster = nextRoster;
        events.Clear();
        RebuildInputs();
    }

    private int FindRoom(int peer, int id)
    {
        for (int room = 0; room < Roster.Slots.Count; room++)
            if (Roster.Slots[room].Player is { } player && player.Peer == peer && player.Id == id)
                return room;
        return -1;
    }

    private void RebuildInputs()
    {
        inputRooms = Enumerable
            .Range(0, LobbyRoster.MaxPlayers)
            .Where(room => Roster.Slots[room].Player != null)
            .ToArray();
        inputPlayers = inputRooms.Select(room => Roster.Slots[room].Player!).ToArray();
    }

    private int RandomIndex(int length)
    {
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        return (int)(randomState % (uint)length);
    }

    private static void WritePlayer(BinaryWriter writer, LobbyPlayer player)
    {
        writer.Write(player.Id);
        writer.Write(player.Peer);
        writer.Write(player.Team);
        writer.Write(player.Color);
        writer.Write(player.Spawned);
        writer.Write(player.Cpu);
    }

    private static LobbyPlayer ReadPlayer(BinaryReader reader) =>
        new(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadBoolean(),
            reader.ReadBoolean()
        );
}
