using System.Text;
using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;

namespace FrogSmashers.Tests;

internal static class LobbyTests
{
    public static void Run(Action<bool, string> check)
    {
        using var hostWire = new ManualWire();
        using var clientWire = new ManualWire();
        using var host = new RelayLobby(hostWire, null, 6, [new(0, Team: 2, Spawned: true)], "fingerprint", "{}");
        using var client = new RelayLobby(
            clientWire,
            "host",
            8,
            [new(0, Team: 1), new(1, Team: 0, Spawned: true)],
            "fingerprint",
            ""
        );
        foreach (
            string malformed in new[]
            {
                "null",
                "{",
                "[]",
                "{\"Kind\":null}",
                "{\"Nonce\":null}",
                "{\"Slots\":null}",
                "{\"Players\":null}",
                "{\"Players\":[null]}",
                "{\"Settings\":null}",
                "{\"Hash\":null}",
                "{\"ClientNonce\":null}",
                "{\"Slots\":{}}",
            }
        )
        {
            hostWire.Incoming.Enqueue(new("stranger", Packet(malformed)));
            host.Poll();
            check(
                host.Error == null && !host.Ready && host.Roster.Count == 1,
                "Malformed lobby control changed host state"
            );
        }
        void Pump()
        {
            for (int i = 0; i < 8; i++)
            {
                client.Poll();
                Route(clientWire, hostWire, "client");
                host.Poll();
                Route(hostWire, clientWire, "host");
            }
        }
        Pump();
        check(
            client.Connected && host.Roster.Count == 3 && client.Roster.Count == 3,
            "Mixed local party did not join the forming lobby"
        );
        check(!host.Starting && !client.Ready, "Lobby started without host action");
        check(
            host.Roster.Slots.Where(slot => slot.Player != null).Select(slot => slot.Player!.Color).Distinct().Count()
                == 3,
            "Joining party reused occupied colors"
        );
        check(host.EditSlot(5, SlotType.Open, false), "Capacity edit rejected");
        Pump();
        check(client.Roster.Capacity == 5, "Capacity change was not rebroadcast");
        check(host.EditSlot(4, SlotType.Open, false), "Slot close rejected");
        Pump();
        check(
            client.Roster.Capacity == 4 && !client.Roster.Slots[4].Open,
            "Slot edits did not update advertised capacity"
        );
        check(host.EditSlot(4, SlotType.Open, true), "Slot reopen rejected");
        Pump();
        check(client.Roster.Capacity == 5, "Reopened slot capacity was not rebroadcast");
        var choosing = client.Roster.Players(client.LocalPeer);
        check(
            client.SetPlayers(choosing.Select(p => p.Id == 0 ? p with { Color = 6 } : p).ToArray()),
            "Color choice was rejected"
        );
        Pump();
        check(
            host.Roster.Players(client.LocalPeer).Any(p => p.Id == 0 && p.Color == 6 && !p.Spawned),
            "Pending frog color did not reach the host"
        );
        check(!host.StartMatch("{}"), "Unspawned player was allowed into match");
        var players = client.Roster.Players(client.LocalPeer);
        check(
            client.SetPlayers(players.Select(player => player with { Spawned = true }).ToArray()),
            "Player confirmation rejected"
        );
        Pump();
        check(host.Roster.Players(1).All(player => player.Spawned), "Confirmation did not reach host");
        var inputs = new InputFrame[8];
        inputs[0] = new(1, 0, InputButtons.Attack);
        inputs[1] = new(-1, 1, InputButtons.Jump);
        client.SendLobbyInputs(inputs);
        Pump();
        check(
            host.ReadLobbyInputs()[0] == default && host.ReadLobbyInputs()[1] == inputs[1],
            "Lobby input escaped slot ownership"
        );
        host.SendSnapshot([1, 2, 3]);
        Pump();
        check(client.TakeSnapshot()!.SequenceEqual(new byte[] { 1, 2, 3 }), "Lobby snapshot was not delivered");
        check(host.StartMatch("{\"seed\":9}"), "Host could not start ready roster");
        Pump();
        check(host.Ready && client.Ready, "Manual lobby start barrier failed");
        check(!host.EditSlot(7, SlotType.Open, true) && !client.SetPlayers([]), "Frozen match accepted roster changes");
        check(
            host.PeerSlots.Length == 2
                && host.PeerSlots[0].SequenceEqual(new[] { 0 })
                && host.PeerSlots[1].SequenceEqual(new[] { 1, 2 }),
            "Mixed local match roster incorrect"
        );
        check(
            host.PlayerTeams.SequenceEqual(new[] { 2, 1, 0 }) && client.PlayerTeams.SequenceEqual(host.PlayerTeams),
            "Teams were not negotiated"
        );
        check(
            client.PlayerColors.SequenceEqual(host.PlayerColors) && client.MatchSettingsJson == "{\"seed\":9}",
            "Match settings or colors differ"
        );
        client.Send(0, [1, 2, 3]);
        Pump();
        check(
            host.TryReceive(out var received)
                && received.Peer == 1
                && received.Data.SequenceEqual(new byte[] { 1, 2, 3 }),
            "Match transport routing failed"
        );
        client.Send(0, [4, 5]);
        var stale = clientWire.Sent.Last().Data.ToArray();
        stale[5] ^= 0xff;
        clientWire.Sent.Clear();
        hostWire.Incoming.Enqueue(new("client", stale));
        host.Poll();
        check(!host.TryReceive(out _), "Old session nonce accepted");
        using var lateWire = new ManualWire();
        using var late = new RelayLobby(lateWire, "host", 8, [new(0)], "fingerprint", "");
        late.Poll();
        Route(lateWire, hostWire, "late");
        host.Poll();
        Route(hostWire, lateWire, "host");
        late.Poll();
        check(late.Error != null && !late.Connected, "Joining remained open after host started");
        TestReturnToLobby(host, client, hostWire, clientWire, Pump, check);
        TestRoomEdits(check);
        TestPlayerRemoval(check);
        var rooms = new LobbyRoster();
        rooms.Edit(4, SlotType.Cpu, true);
        using var roomWire = new ManualWire();
        using var roomHost = new RelayLobby(roomWire, null, 6, rooms.Players(0), "fingerprint", "{}", rooms.Slots);
        check(
            roomHost.Roster.Slots[4].Player?.Cpu == true && roomHost.Roster.Slots[0].Player == null,
            "Opening online moved the existing local room assignments"
        );
        roomHost.EditSlot(0, SlotType.Cpu, true);
        check(
            roomHost.Roster.Players(0).Select(player => player.Id).Distinct().Count() == 2,
            "Adding a CPU reused another room's device identity"
        );
        Console.WriteLine(
            "Lobby joining, capacity broadcasts, room edits, input ownership and manual start checks passed"
        );
    }

    private static void TestReturnToLobby(
        RelayLobby host,
        RelayLobby client,
        ManualWire hostWire,
        ManualWire clientWire,
        Action pump,
        Action<bool, string> check
    )
    {
        var rooms = host.Roster.Slots.ToArray();
        for (int round = 1; round <= 3; round++)
        {
            using var oldHostTransport = host.CreateTransport();
            using var oldClientTransport = client.CreateTransport();
            client.Send(0, [42]);
            byte[] oldPacket = clientWire.Sent.Last(packet => packet.Data[4] == 2).Data.ToArray();
            clientWire.Sent.Clear();
            check(!client.ReturnToLobby(), "guest cannot return everyone to lobby");
            check(host.ReturnToLobby(), "host can return from match");
            oldHostTransport.Dispose();
            oldClientTransport.Dispose();
            pump();
            check(host.Generation == round && client.Generation == round, "return reaches the client");
            check(
                host.Connected && client.Connected && !host.Starting && !client.Starting,
                "return preserves connections and unlocks lobby"
            );
            check(
                host.Roster.Slots.SequenceEqual(rooms) && client.Roster.Slots.SequenceEqual(rooms),
                "return preserves rooms, local party, colors, teams and capacity"
            );
            if (round == 1)
            {
                using var joinWire = new ManualWire();
                using var joining = new RelayLobby(joinWire, "host", 8, [new(0, Spawned: true)], "fingerprint", "");
                joining.Poll();
                Route(joinWire, hostWire, "new guest");
                host.Poll();
                Route(hostWire, joinWire, "host");
                joining.Poll();
                check(joining.Connected && host.Roster.Count == 4, "return reopens joining for a new party");
                joining.Dispose();
                Route(joinWire, hostWire, "new guest");
                pump();
                check(host.Roster.Count == 3 && host.Error == null, "guest can leave forming lobby cleanly");
            }
            hostWire.Incoming.Enqueue(new("client", oldPacket));
            host.Poll();
            check(!host.TryReceive(out _), "old match packet is rejected in lobby");
            var lobbyInput = new InputFrame[8];
            lobbyInput[1] = new(-1, 0, InputButtons.Jump);
            client.SendLobbyInputs(lobbyInput);
            pump();
            check(host.ReadLobbyInputs()[1] == lobbyInput[1], "lobby input works after return");
            host.SendSnapshot([9, 8, 7]);
            pump();
            check(
                client.TakeSnapshot()!.SequenceEqual(new byte[] { 9, 8, 7 }),
                "snapshot sequence restarts after return"
            );
            check(host.StartMatch("{}"), "host can start another match");
            pump();
            check(host.Ready && client.Ready, "second start barrier completes");
            hostWire.Incoming.Enqueue(new("client", oldPacket));
            oldClientTransport.Send(0, [99]);
            pump();
            check(!host.TryReceive(out _), "old packet and old transport cannot affect next match");
            using var current = client.CreateTransport();
            current.Send(0, [7]);
            pump();
            check(
                host.TryReceive(out var packet) && packet.Data.SequenceEqual(new byte[] { 7 }),
                "new match transport sends normally"
            );
        }
        client.Dispose();
        pump();
        check(
            host.Error == null && host.Connected && !host.Starting && host.Roster.Count == 1,
            "guest quitting a match preserves the host lobby and removes their party"
        );
        check(host.Notice == "PLAYER LEFT", "guest departure supplies a concise notice");
    }

    private static void TestRoomEdits(Action<bool, string> check)
    {
        var roster = new LobbyRoster();
        roster.SetCapacity(3);
        roster.SetPlayers(0, [new(0, Color: 2)]);
        roster.Edit(1, SlotType.Local, true);
        check(
            !roster.SetPlayers(1, [new(0), new(1)]) && roster.Count == 1,
            "Partial party was admitted when only one remote slot was free"
        );
        check(roster.SetPlayers(1, [new(0)]), "Single remote player could not join open slot");
        check(
            roster.Slots[1].Player == null && roster.Slots[2].Player?.Peer == 1,
            "Remote player occupied local-only slot"
        );
        check(!roster.SetCapacity(1) && roster.Capacity == 3, "Capacity removed an occupied slot");
        check(
            roster.Edit(1, SlotType.Cpu, true) && roster.Slots[1].Player is { Cpu: true, Spawned: true, Peer: 0 },
            "CPU was not assigned to the selected room"
        );
        check(
            roster.Edit(2, SlotType.Open, true, remove: true) && roster.SetPlayers(2, [new(0)]),
            "Removed slot did not reopen"
        );
        check(
            roster.Edit(1, SlotType.Cpu, false)
                && roster.Capacity == 2
                && roster.Slots[1] is { Type: SlotType.Cpu, Open: false, Player: null },
            "Closing a CPU slot lost its type or capacity"
        );
        check(
            roster.Edit(1, SlotType.Cpu, true) && roster.Slots[1].Player?.Cpu == true && roster.Capacity == 3,
            "Opening a CPU slot did not restore the CPU and capacity"
        );
        check(
            roster.Edit(1, SlotType.Local, true) && roster.Slots[1].Player == null,
            "Changing CPU to local left a bot in the room"
        );
        check(!roster.SetPlayers(2, [new(0), new(0)]), "Duplicate input device claimed multiple slots");
    }

    private static void TestPlayerRemoval(Action<bool, string> check)
    {
        using var hostWire = new ManualWire();
        using var clientWire = new ManualWire();
        using var host = new RelayLobby(
            hostWire,
            null,
            8,
            [new(0, Spawned: true), new(1, Spawned: true)],
            "hash",
            "{}"
        );
        using var client = new RelayLobby(clientWire, "host", 8, [new(0), new(1)], "hash", "");
        void Pump()
        {
            for (int i = 0; i < 8; i++)
            {
                client.Poll();
                Route(clientWire, hostWire, "client");
                host.Poll();
                Route(hostWire, clientWire, "host");
            }
        }
        Pump();
        check(host.EditSlot(2, SlotType.Open, true, remove: true), "Remote player removal failed");
        Pump();
        check(
            client.Error == null && client.Roster.Players(client.LocalPeer) is [{ Id: 1 }],
            "Removing one remote player removed their whole local party or restored the removed player"
        );
        check(host.EditSlot(3, SlotType.Open, false), "Closing the last remote slot failed");
        Pump();
        check(
            client.Error != null && host.Roster.Count == 2 && host.StartMatch("{}"),
            "A connection with no remaining players prevented match start after a kick"
        );
    }

    private static byte[] Packet(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var packet = new byte[bytes.Length + 5];
        BitConverter.TryWriteBytes(packet, 0x46534C32u);
        packet[4] = 1;
        bytes.CopyTo(packet, 5);
        return packet;
    }

    private static void Route(ManualWire from, ManualWire to, string source)
    {
        foreach (var packet in from.Sent)
            to.Incoming.Enqueue(new(source, packet.Data));
        from.Sent.Clear();
    }

    private sealed class ManualWire : IWire
    {
        public Queue<WireMessage> Incoming { get; } = new();
        public List<(string Address, byte[] Data)> Sent { get; } = new();
        public string? Error => null;

        public void Poll() { }

        public void Send(string address, byte[] data, bool reliable) => Sent.Add((address, data));

        public bool Receive(out WireMessage message) => Incoming.TryDequeue(out message);

        public void Dispose() { }
    }
}
