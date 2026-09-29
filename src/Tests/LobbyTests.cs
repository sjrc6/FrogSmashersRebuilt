using System.Text;
using System.Text.Json;
using FrogSmashers.Network;

namespace FrogSmashers.Tests;

internal static class LobbyTests
{
    public static void Run(Action<bool, string> check)
    {
        using var hostWire = new ManualWire();
        using var host = new RelayLobby(hostWire, null, 2, 1, "fingerprint", "{}", [2]);
        string nonce = Guid.NewGuid().ToString("N");
        foreach (
            string malformed in new[]
            {
                "null",
                "{",
                "[]",
                "{\"Kind\":null}",
                "{\"Nonce\":null}",
                "{\"Counts\":null}",
                "{\"Settings\":null}",
                "{\"Hash\":null}",
                "{\"ClientNonce\":null}",
                "{\"Teams\":null}",
                "{\"Teams\":[8]}",
                "{\"Counts\":{}}",
                "{\"Counts\":[1,1,1,1,1,1,1,1,1]}",
                "{\"Kind\":\"hello\",\"Players\":1,\"Hash\":\"fingerprint\",\"Nonce\":\"zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz\"}",
            }
        )
        {
            hostWire.Incoming.Enqueue(new("stranger", Packet(malformed)));
            host.Poll();
            check(
                host.Error == null && !host.Ready && host.PeerSlots.Length == 0,
                "Malformed lobby control changed host state"
            );
        }

        var clientWire = new ManualWire();
        using var client = new RelayLobby(clientWire, "host", 0, 2, "fingerprint", "", [-1, 0]);
        clientWire.Incoming.Enqueue(
            new(
                "host",
                Packet(
                    JsonSerializer.Serialize(
                        new RelayLobby.Control
                        {
                            Kind = LobbyMessageKind.Welcome,
                            Peer = 1,
                            Counts = [1, 2],
                            Hash = "fingerprint",
                            Nonce = new string('z', 32),
                            ClientNonce = nonce,
                            Settings = "{}",
                        }
                    )
                )
            )
        );
        client.Poll();
        check(!client.Ready && client.PeerSlots.Length == 0, "Bad welcome was accepted");
        for (int n = 0; n < 6; n++)
        {
            Route(clientWire, hostWire, "client");
            host.Poll();
            Route(hostWire, clientWire, "host");
            client.Poll();
        }

        check(host.Ready && client.Ready, "Lobby start barrier failed");
        check(
            host.PeerSlots.Length == 2
                && host.PeerSlots[0].SequenceEqual(new[] { 0 })
                && host.PeerSlots[1].SequenceEqual(new[] { 1, 2 }),
            "Mixed local roster incorrect"
        );
        check(
            host.PlayerTeams.SequenceEqual(new[] { 2, 1, 0 }) && client.PlayerTeams.SequenceEqual(host.PlayerTeams),
            "Local team selections were not negotiated"
        );
        check(
            client.MatchSettingsJson == "{}" && client.LocalPeer == 1,
            "Host settings or assigned identity incorrect"
        );
        client.CreateTransport().Send(0, new byte[] { 1, 2, 3 });
        Route(clientWire, hostWire, "client");
        host.Poll();
        check(
            host.CreateTransport().TryReceive(out var data)
                && data.Peer == 1
                && data.Data.SequenceEqual(new byte[] { 1, 2, 3 }),
            "Lobby transport routing failed"
        );
        client.CreateTransport().Send(0, new byte[] { 4, 5 });
        var stale = clientWire.Sent.Last().Data.ToArray();
        stale[5] ^= 0xff;
        clientWire.Sent.Clear();
        hostWire.Incoming.Enqueue(new("client", stale));
        host.Poll();
        check(!host.CreateTransport().TryReceive(out _), "Old session nonce accepted");
        Console.WriteLine("Lobby malformed JSON, roster/start barrier, mixed slots and stale-session checks passed");
    }

    private static byte[] Packet(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var packet = new byte[bytes.Length + 5];
        BitConverter.TryWriteBytes(packet, 0x46534C31u);
        packet[4] = 1;
        bytes.CopyTo(packet, 5);
        return packet;
    }

    private static void Route(ManualWire from, ManualWire to, string source)
    {
        foreach (var packet in from.Sent)
        {
            to.Incoming.Enqueue(new(source, packet.Data));
        }

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
