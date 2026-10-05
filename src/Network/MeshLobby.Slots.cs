namespace FrogSmashers.Network;

internal sealed partial class MeshLobby
{
    private const int SlotEditSettleMilliseconds = 200;
    private readonly SlotType?[] pendingSlotTypes = new SlotType?[LobbyRoster.MaxPlayers];
    private readonly LobbyPlayer?[] pendingCpuPlayers = new LobbyPlayer?[LobbyRoster.MaxPlayers];
    private CpuEdit? cpuEdit;
    private long lastSlotEdit;
    private bool HasPendingSlotEdits =>
        cpuEdit != null
        || pendingSlotTypes.Any(type => type != null)
        || pendingCpuPlayers.Any(player => player != null);

    private sealed record CpuEdit(LobbyCpuCommand Command, SlotType?[] Types);

    public LobbyCpuCommand HostCommand => cpuEdit?.Command ?? default;

    public SlotType GetSlotType(int room) => pendingSlotTypes[room] ?? cpuEdit?.Types[room] ?? Roster.Slots[room].Type;

    public bool IsSlotEditPending(int room) =>
        pendingSlotTypes[room] != null || pendingCpuPlayers[room] != null || cpuEdit?.Types[room] != null;

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

    public bool ChangeCpu(int room, bool team, int direction)
    {
        if (
            !IsHost
            || !Connected
            || Starting
            || matchRequested
            || room is < 0 or >= LobbyRoster.MaxPlayers
            || GetSlotType(room) != SlotType.Cpu
            || Roster.Slots[room].Player is not { Cpu: true }
        )
            return false;
        RefreshSelections();
        var requestedRoster = CopyRoster();
        var slots = requestedRoster.Slots.ToArray();
        for (int index = 0; index < slots.Length; index++)
        {
            var player = pendingCpuPlayers[index];
            if (player == null && cpuEdit is { } sent && (sent.Command.Enabled & (1 << index)) != 0)
                player = new LobbyPlayer(
                    10 + index,
                    Color: sent.Command.Color(index),
                    Team: sent.Command.Team(index),
                    Cpu: true,
                    Spawned: true
                );
            if (player != null)
                slots[index] = new LobbySlot(SlotType.Cpu, player);
        }
        requestedRoster.Replace(slots, requestedRoster.Spectators);
        if (!requestedRoster.ChangeCpu(room, team, direction))
            return false;
        pendingCpuPlayers[room] = requestedRoster.Slots[room].Player;
        lastSlotEdit = Now;
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
            var desired = pendingCpuPlayers[room];
            pendingCpuPlayers[room] = null;
            var type = pendingSlotTypes[room] ?? Roster.Slots[room].Type;
            bool typeChanged = pendingSlotTypes[room] != null && type != Roster.Slots[room].Type;
            pendingSlotTypes[room] = null;
            if (!typeChanged && desired == null)
                continue;
            var current = Roster.Slots[room].Type;
            if (typeChanged && !requestedRoster.Edit(room, type))
                continue;
            if (desired != null && requestedRoster.Slots[room].Player is { Cpu: true })
            {
                var party = requestedRoster
                    .Players(0)
                    .Select(player => player.Id == desired.Id ? desired : player)
                    .ToArray();
                requestedRoster.SetPlayers(0, party);
            }
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
