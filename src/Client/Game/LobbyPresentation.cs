using FrogSmashers.Core;
using FrogSmashers.Network;

namespace FrogSmashers.Client;

internal sealed class LobbyPresentation
{
    private enum RoomChange
    {
        None,
        Joining,
        Leaving,
        Spectating,
    }

    private readonly RoomChange[] changes = new RoomChange[LobbyRoster.MaxPlayers];
    private readonly HashSet<(int Peer, int Id)> announcedJoins = new();
    private readonly HashSet<(long Tick, int Room, SimulationEventKind Kind)> suppressed = new();
    private readonly HashSet<(long Tick, int Room, SimulationEventKind Kind)> sounded = new();
    public LobbyRoster Roster { get; } = new();
    public bool Pending => changes.Any(change => change != RoomChange.None);

    public string? Status(int room) =>
        changes[room] switch
        {
            RoomChange.Joining => "JOINING...",
            RoomChange.Leaving => "LEAVING...",
            RoomChange.Spectating => "SPECTATING...",
            _ => null,
        };

    public void Update(LobbyRoster committed, LobbyRoster proposed)
    {
        var slots = committed.Slots.ToArray();
        for (int room = 0; room < slots.Length; room++)
        {
            var current = slots[room].Player;
            var next = proposed.Slots[room].Player;
            changes[room] = RoomChange.None;
            if (current?.Peer == next?.Peer && current?.Id == next?.Id)
                continue;
            if (current is { Cpu: false })
            {
                changes[room] = proposed.Spectator(current.Peer) != null ? RoomChange.Spectating : RoomChange.Leaving;
                slots[room] = proposed.Slots[room];
            }
            if (next is { Cpu: false })
            {
                slots[room] = proposed.Slots[room] with { Player = next with { Spawned = false } };
                changes[room] = RoomChange.Joining;
            }
        }
        for (int room = 0; room < slots.Length; room++)
        {
            if (changes[room] != RoomChange.Joining || slots[room].Player is not { } joining)
                continue;
            var used = slots.Where((_, index) => index != room).Select(slot => slot.Player?.Color).ToHashSet();
            slots[room] = slots[room] with
            {
                Player = joining with
                {
                    Color = used.Contains(joining.Color)
                        ? Enumerable.Range(0, LobbyRoster.MaxPlayers).First(color => !used.Contains(color))
                        : joining.Color,
                    Team = LobbyRoster.AvailableTeam(
                        joining.Team,
                        slots.Where((_, index) => index != room).Select(slot => slot.Player)
                    ),
                },
            };
        }
        Roster.Replace(
            slots,
            committed
                .Spectators.Where(spectator =>
                    !slots.Any(slot => slot.Player is { Cpu: false } player && player.Peer == spectator.Peer)
                )
                .ToArray()
        );
        announcedJoins.RemoveWhere(identity =>
            !proposed.Players(identity.Peer).Any(player => player.Id == identity.Id)
        );
    }

    public bool AnnounceJoin(int peer, int id) => announcedJoins.Add((peer, id));

    public bool ShowEvent(SimulationEvent item, LobbyPlayer? player)
    {
        var key = (item.Tick, item.Player, item.Kind);
        if (suppressed.Contains(key))
            return false;
        if (
            item.Kind is SimulationEventKind.LobbyPreview or SimulationEventKind.Spawn
            && player != null
            && announcedJoins.Remove((player.Peer, player.Id))
        )
        {
            suppressed.Add(key);
            return false;
        }
        return true;
    }

    public bool PlaySound(SimulationEvent item) => sounded.Add((item.Tick, item.Player, item.Kind));

    public void Confirm(long tick)
    {
        suppressed.RemoveWhere(key => key.Tick < tick);
        sounded.RemoveWhere(key => key.Tick < tick);
    }

    public void Clear()
    {
        Roster.Reset();
        Array.Clear(changes);
        announcedJoins.Clear();
        suppressed.Clear();
        sounded.Clear();
    }
}
