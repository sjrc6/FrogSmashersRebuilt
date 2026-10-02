using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class LobbyRosterTests
{
    public static void Run()
    {
        foreach (var type in Enum.GetValues<SlotType>())
        foreach (bool local in new[] { false, true })
        foreach (bool invited in new[] { false, true })
        foreach (bool friend in new[] { false, true })
        {
            var roster = new LobbyRoster();
            roster.ApplySlotType(SlotType.Closed);
            roster.Edit(0, type);
            int peer = local ? 0 : 1;
            bool expected =
                type == SlotType.Open
                || type == SlotType.Local && local
                || type == SlotType.Private && (local || invited)
                || type == SlotType.Friend && (local || friend);
            Check(
                roster.SetPlayers(peer, [.. roster.Players(peer), new(0)], new(invited, friend)) == expected,
                $"Admission for {type}, local={local}, invited={invited}, friend={friend}"
            );
        }
        var rooms = new LobbyRoster();
        rooms.ApplySlotType(SlotType.Closed);
        rooms.Edit(7, SlotType.Open);
        rooms.SetPlayers(0, [new(0)]);
        rooms.ConfigureEmpty(SlotType.Private, 1);
        Check(
            rooms.Slots[7].Player?.Id == 0 && rooms.Capacity == 1,
            "Creation preserves an occupied room above the selected capacity"
        );
        rooms.ApplySlotType(SlotType.Friend);
        Check(
            rooms.Slots[7].Type == SlotType.Open && rooms.Slots.Take(7).All(s => s.Type == SlotType.Friend),
            "Apply to all includes closed rooms and preserves occupied policy"
        );
        Check(!rooms.SetPlayers(1, [new(10, Cpu: true)]), "Guests cannot create CPUs");
        Check(
            !rooms.Edit(7, SlotType.Cpu) && rooms.Slots[7].Player?.Cpu == false,
            "CPU selection cannot replace an occupied player"
        );

        var cpuRooms = new LobbyRoster(SlotType.Local);
        Check(cpuRooms.Slots.All(slot => slot.Type == SlotType.Local), "Local rooms start with Local admission");
        cpuRooms.SetPlayers(0, [new(0)]);
        for (int room = 1; room < LobbyRoster.MaxPlayers; room++)
            Check(cpuRooms.Edit(room, SlotType.Cpu), "CPU type creates a bot in an empty room");
        var cpu = cpuRooms.Slots[1].Player;
        Check(cpuRooms.Edit(1, SlotType.Cpu) && cpuRooms.Slots[1].Player == cpu, "Reselecting CPU preserves the bot");
        Check(
            cpuRooms.Edit(1, SlotType.Local) && cpuRooms.Slots[1].Player == null && cpuRooms.Count == 7,
            "Changing a CPU room back to Local removes its bot, even in a full lobby"
        );
        cpuRooms.Edit(1, SlotType.Cpu);
        cpuRooms.ApplySlotType(SlotType.Local);
        Check(
            cpuRooms.Count == 1 && cpuRooms.Slots[0].Player?.Id == 0,
            "Apply to all replaces CPUs but protects humans"
        );
        cpuRooms.Edit(1, SlotType.Cpu);
        cpu = cpuRooms.Slots[1].Player!;
        cpuRooms.RemovePlayer(cpu.Peer, cpu.Id);
        Check(cpuRooms.Slots[1].Type == SlotType.Local, "Kicking a local CPU restores a Local room");
        cpuRooms.Edit(1, SlotType.Cpu);
        cpu = cpuRooms.Slots[1].Player;
        cpuRooms.ConfigureEmpty(SlotType.Private, 4);
        Check(
            cpuRooms.Slots[1].Player == cpu
                && cpuRooms.Slots[1].Type == SlotType.Cpu
                && cpuRooms.Capacity == 4
                && cpuRooms.Slots.Skip(2).Take(2).All(slot => slot.Type == SlotType.Private)
                && cpuRooms.Slots.Skip(4).All(slot => slot.Type == SlotType.Closed),
            "Opening online preserves CPUs and converts unused rooms to the selected type and capacity"
        );
        cpuRooms.Reset();
        Check(
            cpuRooms.Count == 0 && cpuRooms.Slots.All(slot => slot.Type == SlotType.Local),
            "Reset restores local room types"
        );

        var observers = new LobbyRoster();
        for (int peer = 0; peer < 5; peer++)
            Check(observers.SetPlayers(peer, [new(0)]), "Spectator candidate can join");
        for (int peer = 1; peer < 5; peer++)
            Check(observers.SetSpectating(peer, true), "Connection moves to spectator list");
        Check(
            observers.Count == 1 && observers.Spectators.Count == 4 && !observers.SetSpectating(0, true),
            "Four spectators are separate from player slots and cannot displace each other"
        );
        var rejoining = new LobbyRoster();
        rejoining.Replace(observers.Slots, observers.Spectators);
        Check(
            rejoining.SetPlayers(1, [new(2)]) && rejoining.Spectator(1) == null && rejoining.Humans(1).Single().Id == 2,
            "A spectator can join directly with another device and leaves the spectator list atomically"
        );
        observers.ApplySlotType(SlotType.Local);
        Check(
            !observers.SetSpectating(1, false) && observers.Spectator(1) != null,
            "Unspectating respects available online slots and is atomic"
        );
        observers.Edit(1, SlotType.Private);
        Check(!observers.SetSpectating(1, false), "Private slot requires an invitation when unspectating");
        Check(
            observers.SetSpectating(1, false, new(Invited: true)) && observers.Slots[1].Player?.Peer == 1,
            "Invited spectator returns to first eligible room"
        );
        var copy = new LobbyRoster();
        copy.Replace(observers.Slots, observers.Spectators);
        Check(copy.Spectators.SequenceEqual(observers.Spectators), "Roster copies retain spectators");
        copy.RemovePeer(2);
        Check(copy.Spectator(2) == null, "Disconnect removes the spectator entry");
        Console.WriteLine("Slot policies, protected rooms, creation capacity and spectator roster passed");
    }
}
