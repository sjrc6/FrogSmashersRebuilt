namespace FrogSmashers.Network;

public enum SlotType
{
    Open,
    Local,
    Cpu,
}

public sealed record LobbyPlayer(
    int Id,
    int Peer = 0,
    int Team = 0,
    int Color = 0,
    bool Spawned = false,
    bool Cpu = false
);

public sealed record LobbySlot(SlotType Type = SlotType.Open, bool Open = true, LobbyPlayer? Player = null);

public sealed class LobbyRoster
{
    private LobbySlot[] slots = Enumerable.Range(0, 8).Select(_ => new LobbySlot()).ToArray();
    public IReadOnlyList<LobbySlot> Slots => slots;
    public int Capacity => slots.Count(slot => slot.Open);
    public int Count => slots.Count(slot => slot.Player != null);

    public LobbyPlayer[] Players(int peer) =>
        slots.Where(slot => slot.Player?.Peer == peer).Select(slot => slot.Player!).ToArray();

    public bool SetCapacity(int capacity)
    {
        if (capacity is < 0 or > 8 || capacity < Count)
            return false;
        for (int room = 7; room >= 0 && Capacity > capacity; room--)
            if (slots[room].Open && slots[room].Player == null)
                Edit(room, slots[room].Type, false);
        for (int room = 0; room < 8 && Capacity < capacity; room++)
            if (!slots[room].Open)
                Edit(room, slots[room].Type, true);
        return true;
    }

    public bool SetPlayers(int peer, IReadOnlyList<LobbyPlayer> players)
    {
        if (
            peer is < 0 or > 7
            || players.Count > 8
            || players.Any(p => !ValidPlayer(p))
            || players.Select(p => p.Id).Distinct().Count() != players.Count
        )
            return false;
        var next = slots.ToArray();
        for (int room = 0; room < 8; room++)
            if (next[room].Player is { } old && old.Peer == peer && !players.Any(p => p.Id == old.Id))
                next[room] = next[room] with { Player = null, Type = old.Cpu ? SlotType.Open : next[room].Type };
        foreach (var requested in players)
        {
            int room = Array.FindIndex(next, slot => slot.Player?.Peer == peer && slot.Player.Id == requested.Id);
            if (room < 0)
                room = Array.FindIndex(
                    next,
                    slot =>
                        slot.Open
                        && slot.Player == null
                        && (slot.Type == SlotType.Open || peer == 0 && slot.Type == SlotType.Local)
                );
            if (room < 0 || next[room].Player is { } existing && existing.Cpu != requested.Cpu)
                return false;
            var used = next.Where((_, index) => index != room).Select(slot => slot.Player?.Color).ToHashSet();
            int color = requested.Color;
            if (used.Contains(color))
                color = Enumerable.Range(0, 8).First(value => !used.Contains(value));
            next[room] = next[room] with
            {
                Type = requested.Cpu ? SlotType.Cpu : next[room].Type,
                Player = requested with { Peer = peer, Color = color },
            };
        }
        slots = next;
        return true;
    }

    public bool Edit(int room, SlotType type, bool open, bool remove = false)
    {
        if (room is < 0 or > 7 || !Enum.IsDefined(type))
            return false;
        var player = remove || !open ? null : slots[room].Player;
        if (player != null && (player.Cpu != (type == SlotType.Cpu) || type == SlotType.Local && player.Peer != 0))
            player = null;
        if (open && type == SlotType.Cpu && player == null)
        {
            var others = slots.Where((_, index) => index != room).Select(slot => slot.Player).ToArray();
            int color = Enumerable.Range(0, 8).First(value => others.All(p => p?.Color != value));
            int id = Enumerable.Range(10, 8).First(value => others.All(p => p?.Peer != 0 || p.Id != value));
            player = new LobbyPlayer(id, Team: room % 2, Color: color, Spawned: true, Cpu: true);
        }
        slots[room] = new(type, open, player);
        return true;
    }

    public void Replace(IReadOnlyList<LobbySlot> state)
    {
        if (
            state.Count != 8
            || state.Any(slot =>
                slot == null
                || !Enum.IsDefined(slot.Type)
                || !slot.Open && slot.Player != null
                || slot.Open && slot.Type == SlotType.Cpu && slot.Player == null
                || slot.Player is { } p
                    && (
                        !ValidPlayer(p)
                        || p.Cpu != (slot.Type == SlotType.Cpu)
                        || slot.Type == SlotType.Local && p.Peer != 0
                    )
            )
        )
            throw new ArgumentException("Invalid lobby roster");
        var players = state.Where(slot => slot.Player != null).Select(slot => slot.Player!).ToArray();
        if (
            players.Select(p => (p.Peer, p.Id)).Distinct().Count() != players.Length
            || players.Select(p => p.Color).Distinct().Count() != players.Length
        )
            throw new ArgumentException("Duplicate lobby players or colors");
        slots = state.ToArray();
    }

    private static bool ValidPlayer(LobbyPlayer player) =>
        player.Id is >= 0 and < 18
        && player.Peer is >= 0 and < 8
        && player.Team is >= 0 and < 8
        && player.Color is >= 0 and < 8
        && (player.Cpu ? player.Id >= 10 : player.Id < 10);
}
