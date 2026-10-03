namespace FrogSmashers.Network;

public enum SlotType
{
    Open,
    Local,
    Private,
    Friend,
    Closed,
    Cpu,
}

public readonly record struct LobbyAccess(bool Invited = false, bool Friend = false)
{
    public bool Allows(SlotType type, int peer) =>
        type switch
        {
            SlotType.Open => true,
            SlotType.Local => peer == 0,
            SlotType.Private => peer == 0 || Invited,
            SlotType.Friend => peer == 0 || Friend,
            _ => false,
        };
}

public sealed record LobbyPlayer(
    int Id,
    int Peer = 0,
    int Team = 0,
    int Color = 0,
    bool Spawned = false,
    bool Cpu = false
)
{
    internal void WriteSnapshot(BinaryWriter writer)
    {
        writer.Write(Id);
        writer.Write(Peer);
        writer.Write(Team);
        writer.Write(Color);
        writer.Write(Spawned);
        writer.Write(Cpu);
    }

    internal static LobbyPlayer ReadSnapshot(BinaryReader reader) =>
        new(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadBoolean(),
            reader.ReadBoolean()
        );
}

public sealed record LobbySlot(SlotType Type = SlotType.Open, LobbyPlayer? Player = null)
{
    public bool Open => Type != SlotType.Closed;
}

public sealed class LobbyRoster
{
    public const int MaxPlayers = 8;
    public const int MaxTeamPlayers = 4;
    public const int MaxSpectators = 4;
    public const int MaxPeers = MaxPlayers + MaxSpectators;
    private readonly SlotType defaultType;
    private LobbySlot[] slots = [];
    private LobbyPlayer[] spectators = [];
    public IReadOnlyList<LobbySlot> Slots => slots;
    public IReadOnlyList<LobbyPlayer> Spectators => spectators;
    public int Capacity => slots.Count(slot => slot.Open);
    public int Count => slots.Count(slot => slot.Player != null);

    public LobbyMembership Membership() => new(slots.Select(slot => slot.Player), spectators);

    public void ApplyMembership(LobbyMembership membership)
    {
        var next = slots
            .Select(
                (slot, room) =>
                {
                    var player = membership.Rooms[room];
                    var type =
                        player is { Cpu: true } ? SlotType.Cpu
                        : slot.Type == SlotType.Cpu || player != null && slot.Type == SlotType.Closed ? defaultType
                        : slot.Type;
                    return new LobbySlot(type, player);
                }
            )
            .ToArray();
        Replace(next, membership.Spectators);
    }

    public LobbyRoster(SlotType defaultType = SlotType.Open)
    {
        this.defaultType = defaultType;
        Reset();
    }

    public void Reset()
    {
        slots = Enumerable.Range(0, MaxPlayers).Select(_ => new LobbySlot(defaultType)).ToArray();
        spectators = [];
    }

    public LobbyPlayer[] Players(int peer) =>
        slots.Where(slot => slot.Player?.Peer == peer).Select(slot => slot.Player!).ToArray();

    public LobbyPlayer[] Humans(int peer) => Players(peer).Where(player => !player.Cpu).ToArray();

    public LobbyPlayer? Spectator(int peer) => spectators.FirstOrDefault(player => player.Peer == peer);

    public int Available(int peer, LobbyAccess access = default) =>
        slots.Count(slot => slot.Player == null && access.Allows(slot.Type, peer));

    public bool SetCapacity(int capacity)
    {
        if (capacity is < 0 or > MaxPlayers || capacity < Count)
            return false;
        for (int room = MaxPlayers - 1; room >= 0 && Capacity > capacity; room--)
            if (slots[room].Open && slots[room].Player == null)
                Edit(room, SlotType.Closed);
        for (int room = 0; room < MaxPlayers && Capacity < capacity; room++)
            if (!slots[room].Open)
                Edit(room, defaultType);
        return true;
    }

    public void ConfigureEmpty(SlotType type, int capacity)
    {
        if (type is not (SlotType.Open or SlotType.Private or SlotType.Friend))
            throw new ArgumentException("Invalid online lobby type");
        for (int room = 0; room < MaxPlayers; room++)
            if (slots[room].Player == null)
                Edit(room, type);
        SetCapacity(Math.Clamp(capacity, Count, MaxPlayers));
    }

    public bool SetPlayers(int peer, IReadOnlyList<LobbyPlayer> players, LobbyAccess access = default)
    {
        if (
            peer is < 0 or >= MaxPeers
            || players.Count > MaxPlayers
            || players.Any(p => !ValidPlayer(p) || p.Cpu && peer != 0)
            || players.Select(p => p.Id).Distinct().Count() != players.Count
        )
            return false;
        var next = slots.ToArray();
        for (int room = 0; room < MaxPlayers; room++)
            if (next[room].Player is { } old && old.Peer == peer && !players.Any(p => p.Id == old.Id))
                next[room] = Empty(next[room]);
        foreach (var requested in players)
        {
            int room = Array.FindIndex(next, slot => slot.Player?.Peer == peer && slot.Player.Id == requested.Id);
            if (room < 0)
                room = Array.FindIndex(next, slot => slot.Player == null && access.Allows(slot.Type, peer));
            if (room < 0 || next[room].Player is { } existing && existing.Cpu != requested.Cpu)
                return false;
            var used = next.Where((_, index) => index != room).Select(slot => slot.Player?.Color).ToHashSet();
            int color = requested.Color;
            if (used.Contains(color))
                color = Enumerable.Range(0, MaxPlayers).First(value => !used.Contains(value));
            next[room] = next[room] with
            {
                Type = requested.Cpu ? SlotType.Cpu : next[room].Type,
                Player = requested with
                {
                    Peer = peer,
                    Color = color,
                    Team = AvailableTeam(
                        requested.Team,
                        next.Where((_, index) => index != room).Select(slot => slot.Player)
                    ),
                },
            };
        }
        slots = next;
        if (players.Any(player => !player.Cpu))
            spectators = spectators.Where(player => player.Peer != peer).ToArray();
        return true;
    }

    public bool Edit(int room, SlotType type)
    {
        if (room is < 0 or >= MaxPlayers || !Enum.IsDefined(type) || slots[room].Player is { Cpu: false })
            return false;
        if (slots[room].Type == type)
            return true;
        LobbyPlayer? player = null;
        if (type == SlotType.Cpu)
        {
            var others = slots.Select(slot => slot.Player).ToArray();
            int color = Enumerable.Range(0, MaxPlayers).First(value => others.All(p => p?.Color != value));
            int id = 10 + room;
            int team = AvailableTeam(color, others);
            player = new LobbyPlayer(id, Team: team, Color: color, Spawned: true, Cpu: true);
        }
        slots[room] = new(type, player);
        return true;
    }

    public void ApplySlotType(SlotType type)
    {
        for (int room = 0; room < MaxPlayers; room++)
            Edit(room, type);
    }

    public bool RemovePlayer(int peer, int id)
    {
        int room = Array.FindIndex(slots, slot => slot.Player?.Peer == peer && slot.Player.Id == id);
        if (room < 0)
            return false;
        slots[room] = Empty(slots[room]);
        return true;
    }

    public bool SetSpectating(int peer, bool spectating, LobbyAccess access = default)
    {
        var saved = Spectator(peer);
        if (spectating)
        {
            if (saved != null)
                return true;
            var humans = Humans(peer);
            if (humans.Length != 1 || spectators.Length >= MaxSpectators)
                return false;
            RemovePlayer(peer, humans[0].Id);
            spectators = [.. spectators, humans[0] with { Spawned = false }];
            return true;
        }
        if (saved == null)
            return false;
        return SetPlayers(peer, [.. Players(peer), saved with { Spawned = false }], access);
    }

    public void RemovePeer(int peer)
    {
        slots = slots.Select(slot => slot.Player?.Peer == peer ? Empty(slot) : slot).ToArray();
        spectators = spectators.Where(player => player.Peer != peer).ToArray();
    }

    public void Replace(IReadOnlyList<LobbySlot> state, IReadOnlyList<LobbyPlayer>? observers = null)
    {
        observers ??= [];
        if (
            state.Count != MaxPlayers
            || state.Any(slot =>
                slot == null
                || !Enum.IsDefined(slot.Type)
                || !slot.Open && slot.Player != null
                || slot.Type == SlotType.Cpu && slot.Player == null
                || slot.Player is { } p && (!ValidPlayer(p) || p.Cpu != (slot.Type == SlotType.Cpu))
            )
        )
            throw new ArgumentException("Invalid lobby roster");
        _ = new LobbyMembership(state.Select(slot => slot.Player), observers);
        slots = state.ToArray();
        spectators = observers.ToArray();
    }

    private LobbySlot Empty(LobbySlot slot) => new(slot.Type == SlotType.Cpu ? defaultType : slot.Type);

    public static int AvailableTeam(int preferred, IEnumerable<LobbyPlayer?> players, int direction = 1)
    {
        if (preferred is < 0 or >= MaxPlayers || direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(preferred));
        Span<int> counts = stackalloc int[MaxPlayers];
        counts.Clear();
        foreach (var player in players)
            if (player != null)
                counts[player.Team]++;
        for (int offset = 0; offset < MaxPlayers; offset++)
        {
            int team = (preferred + direction * offset + MaxPlayers) % MaxPlayers;
            if (counts[team] < MaxTeamPlayers)
                return team;
        }
        throw new InvalidOperationException("No team has room for another player");
    }

    internal static bool ValidPlayer(LobbyPlayer player) =>
        player.Id is >= 0 and < 18
        && player.Peer is >= 0 and < MaxPeers
        && player.Team is >= 0 and < MaxPlayers
        && player.Color is >= 0 and < MaxPlayers
        && (player.Cpu ? player.Peer == 0 && player.Id >= 10 : player.Id < 10);
}
