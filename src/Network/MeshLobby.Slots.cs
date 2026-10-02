namespace FrogSmashers.Network;

internal sealed partial class MeshLobby
{
    private const int SlotEditSettleMilliseconds = 200;
    private readonly SlotType?[] pendingSlotTypes = new SlotType?[LobbyRoster.MaxPlayers];
    private CpuEdit? cpuEdit;
    private long lastSlotEdit;
    private bool HasPendingSlotEdits => cpuEdit != null || pendingSlotTypes.Any(type => type != null);

    private sealed record CpuEdit(LobbyCpuCommand Command, SlotType?[] Types);

    public LobbyCpuCommand HostCommand => cpuEdit?.Command ?? default;

    public SlotType GetSlotType(int room) => pendingSlotTypes[room] ?? cpuEdit?.Types[room] ?? Roster.Slots[room].Type;

    public bool IsSlotEditPending(int room) => pendingSlotTypes[room] != null || cpuEdit?.Types[room] != null;

    public bool EditSlot(int room, SlotType type)
    {
        if (
            !IsHost
            || Starting
            || matchRequested
            || room is < 0 or >= LobbyRoster.MaxPlayers
            || !Enum.IsDefined(type)
            || Roster.Slots[room].Player is { Cpu: false }
        )
            return false;
        var baseline = cpuEdit?.Types[room] ?? Roster.Slots[room].Type;
        if (type == baseline)
        {
            pendingSlotTypes[room] = null;
            return true;
        }
        if (cpuEdit?.Types[room] == null && Roster.Slots[room].Type != SlotType.Cpu && type != SlotType.Cpu)
        {
            pendingSlotTypes[room] = null;
            Roster.Edit(room, type);
            PublishRoster();
        }
        else
        {
            pendingSlotTypes[room] = type;
            lastSlotEdit = Now;
        }
        return true;
    }

    public bool ApplySlotType(SlotType type)
    {
        if (!IsHost || Starting || matchRequested || !Enum.IsDefined(type))
            return false;
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
            EditSlot(room, type);
        return true;
    }

    private void UpdateCpuEdits()
    {
        if (!IsHost || simulation == null || Starting)
            return;
        if (
            cpuEdit is { } sent
            && LobbySession is { } session
            && session.TryGetConfirmedCheckpoint(session.ConfirmedFrame + 1, out var snapshot)
            && LobbySimulation.ReadCpuRevision(snapshot) >= sent.Command.Revision
        )
        {
            CommitCpuEdit(sent);
            cpuEdit = null;
        }
        if (
            cpuEdit != null
            || checkpoint != null
            || rosterChanged
            || Now - lastSlotEdit < SlotEditSettleMilliseconds
            || LobbySession is not { State: GGCS.SessionState.Running }
        )
            return;
        RefreshSelections();
        var requestedRoster = CopyRoster();
        var types = new SlotType?[LobbyRoster.MaxPlayers];
        byte changedRooms = 0;
        for (int room = 0; room < LobbyRoster.MaxPlayers; room++)
        {
            if (pendingSlotTypes[room] is not { } type)
                continue;
            pendingSlotTypes[room] = null;
            var current = Roster.Slots[room].Type;
            if (current == type || !requestedRoster.Edit(room, type))
                continue;
            if (current == SlotType.Cpu || type == SlotType.Cpu)
            {
                changedRooms |= (byte)(1 << room);
                types[room] = type;
            }
            else
            {
                Roster.Edit(room, type);
                PublishRoster();
            }
        }
        if (changedRooms != 0)
            cpuEdit = new(
                LobbyCpuCommand.FromRoster(checked(simulation.CpuRevision + 1), changedRooms, requestedRoster),
                types
            );
    }

    private void CommitCpuEdit(CpuEdit edit)
    {
        var live = simulation!.Membership.Rooms.OfType<LobbyPlayer>().ToDictionary(player => (player.Peer, player.Id));
        var slots = Roster
            .Slots.Select(
                (slot, room) =>
                {
                    if (edit.Types[room] is { } type)
                        return new LobbySlot(type, simulation.Membership.Rooms[room] is { Cpu: true } cpu ? cpu : null);
                    return slot.Player is { } player && live.TryGetValue((player.Peer, player.Id), out var current)
                        ? slot with
                        {
                            Player = current,
                        }
                        : slot;
                }
            )
            .ToArray();
        Roster.Replace(slots, Roster.Spectators);
        PublishRoster();
    }

    private void PublishRoster()
    {
        revision++;
        lastSend = -1000;
    }
}
