using System.Diagnostics;
using System.Net;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class LobbyDiscoveryTests
{
    public static void Run(bool sockets)
    {
        Addresses();
        SteamListings();
        LanPackets();
        if (sockets)
            RealLanDiscovery();
    }

    private static void Addresses()
    {
        const ulong lobby = 109775243810123456;
        foreach (
            string text in new[]
            {
                $" {lobby}\r\n",
                $"steam:{lobby}",
                $"steam://joinlobby/480/{lobby}/76561198000000000",
                $"https://steamcommunity.com/lobby/480/{lobby}",
            }
        )
            Check(
                LobbyAddress.TrySteam(text, out ulong id) && id == lobby,
                "Clipboard recognizes lobby codes and Steam invite links"
            );
        foreach (
            string text in new[]
            {
                "",
                "0",
                "hello 123",
                "steam://joinlobby/312530/123/456",
                "https://example.com/lobby/480/123",
                "steam://joinlobby:1/480/123",
                "steam://joinlobby/480/123?x=1",
                new string('9', 600),
            }
        )
            Check(!LobbyAddress.TrySteam(text, out _), "Clipboard rejects unrelated or malformed lobby links");
        foreach (string text in new[] { "127.0.0.1:30000", "udp://127.0.0.1:30000", " udp:127.0.0.1:30000\n" })
            Check(
                LobbyAddress.TryUdp(text, 24804, out var host, out int port) && host == "127.0.0.1" && port == 30000,
                "UDP clipboard preserves an explicit port"
            );
        Check(
            LobbyAddress.TryUdp("my-pc.local", 24804, out var name, out int defaultPort)
                && name == "my-pc.local"
                && defaultPort == 24804,
            "Hostnames use the configured UDP default port"
        );
        foreach (
            string text in new[]
            {
                "",
                "bad host",
                "127.0.0.1:0",
                "127.0.0.1:65536",
                "127.0.0.1:24804:2",
                "https://example.com",
                "127.0.0.1/path",
                "127.0.0.1\nother",
                "[::1]:24804",
                "999.999.999.999",
                ":24804",
            }
        )
            Check(
                !LobbyAddress.TryUdp(text, 24804, out _, out _),
                "UDP parsing rejects invalid addresses before connecting"
            );
    }

    private static void SteamListings()
    {
        var data = new Dictionary<string, string>
        {
            ["game"] = SteamLobby.GameTag,
            ["protocol"] = NetworkBuild.Protocol,
            ["compatibility"] = "build",
            ["state"] = "forming",
            ["capacity"] = "8",
            ["players"] = "3",
            ["open_slots"] = "1",
            ["friend_slots"] = "2",
            ["private_slots"] = "2",
            ["name"] = "A HOST\nNAME",
        };
        LobbyListing? Read(bool friend, int size) =>
            SteamLobbyBrowser.ReadListing(123, key => data.GetValueOrDefault(key, ""), "build", friend, size);
        Check(
            Read(false, 1) is { AvailableSlots: 1, Name: "A HOSTNAME" },
            "Steam browser uses open slots and safe single-line names"
        );
        Check(
            Read(false, 2) == null && Read(true, 3)?.AvailableSlots == 3,
            "Friend slots require host friendship and fit the entire local party"
        );
        Check(Read(true, 4) == null, "Private slots do not count as browser admission");
        foreach (string key in new[] { "game", "protocol", "compatibility", "state" })
        {
            string original = data[key];
            data[key] = "different";
            Check(Read(true, 1) == null, "Browser excludes other Spacewar games, builds and started matches");
            data[key] = original;
        }
        data["open_slots"] = "999";
        Check(Read(true, 1) == null, "Invalid Steam capacity metadata is ignored");
    }

    private static void LanPackets()
    {
        var roster = new LobbyRoster();
        roster.SetPlayers(0, [new(0)]);
        roster.SetCapacity(4);
        roster.Edit(2, SlotType.Local);
        roster.Edit(3, SlotType.Private);
        Guid id = Guid.NewGuid();
        var source = new IPEndPoint(IPAddress.Parse("192.168.5.7"), LanDiscovery.Port);
        byte[] query = LanDiscovery.Query(71, "build");
        Check(
            LanDiscovery.ReadQuery(query, "build", out ulong nonce) && nonce == 71,
            "Discovery request retains its nonce"
        );
        Check(!LanDiscovery.ReadQuery(query, "other", out _), "Hosts ignore incompatible discovery requests");
        byte[] reply = LanDiscovery.Reply(71, "build", id, 30000, "Test host", roster, true);
        var listing = LanDiscovery.ReadReply(reply, source, "build", 71);
        Check(
            listing is { Target: "udp:192.168.5.7:30000", Players: 1, Capacity: 4, AvailableSlots: 1 },
            "LAN replies use the observed address, advertised game port and remotely joinable slots"
        );
        Check(
            listing!.Id == id.ToString("N") && reply.Length < 200,
            "Discovery identity deduplicates interfaces in a small packet"
        );
        Check(
            LanDiscovery.ReadReply(reply, source, "build", 72) == null
                && LanDiscovery.ReadReply(reply, source, "other", 71) == null,
            "Stale refreshes and other builds cannot inject listings"
        );
        byte[] started = LanDiscovery.Reply(71, "build", id, 30000, "Test host", roster, false);
        Check(
            LanDiscovery.ReadReply(started, source, "build", 71)?.AvailableSlots == 0,
            "Started matches remove their joinable slots"
        );
        for (int size = 0; size < reply.Length; size++)
            Check(
                LanDiscovery.ReadReply(reply[..size], source, "build", 71) == null,
                "Truncated discovery packets are ignored"
            );
        Check(
            LanDiscovery.ReadReply([.. reply, 0], source, "build", 71) == null,
            "Trailing discovery data is rejected"
        );
        Check(
            LanDiscovery.Broadcast(IPAddress.Parse("10.12.7.33"), IPAddress.Parse("255.255.252.0")).ToString()
                == "10.12.7.255",
            "Directed broadcasts honor the actual subnet mask"
        );
    }

    private static void RealLanDiscovery()
    {
        using var host = UdpLobby.Host(0, 3, [new(0)], "discovery-test", "{}", allowLan: true);
        using var single = new LanLobbyBrowser("discovery-test", 1);
        using var party = new LanLobbyBrowser("discovery-test", 2);
        using var incompatible = new LanLobbyBrowser("other-build", 1);
        void Poll()
        {
            host.Poll();
            single.Poll();
            party.Poll();
            incompatible.Poll();
        }
        Wait(() => single.Results.Count == 1 && party.Results.Count == 1, Poll);
        Check(
            single.Results[0].AvailableSlots == 2 && incompatible.Results.Count == 0,
            "Real LAN queries return one compatible host to concurrent browsers"
        );
        Check(
            LobbyAddress.TryUdp(single.Results[0].Target, 1, out _, out int port)
                && port > 0
                && port != LanDiscovery.Port,
            "LAN discovery advertises the real dynamic game port"
        );
        Check(host.EditSlot(2, SlotType.Closed), "LAN host can close a slot while browsed");
        Wait(() => single.Results.SingleOrDefault()?.Capacity == 2 && party.Results.Count == 0, Poll);
        Check(single.Results[0].AvailableSlots == 1, "Fresh slot edits remove lobbies that no longer fit the party");
        single.Refresh();
        Wait(() => single.Results.Count == 1, Poll);
        string previous = single.Results[0].Id;
        host.Dispose();
        Wait(() => single.Results.Count == 0, single.Poll, 7000);
        using var replacement = UdpLobby.Host(0, 3, [new(0)], "discovery-test", "{}", allowLan: true);
        Wait(
            () => single.Results.Count == 1,
            () =>
            {
                replacement.Poll();
                single.Poll();
            }
        );
        Check(
            single.Results[0].Id != previous,
            "Closed hosts expire and replacement lobbies are discovered without leaving the browser"
        );
    }

    private static void Wait(Func<bool> ready, Action poll, int timeout = 4000)
    {
        var watch = Stopwatch.StartNew();
        while (!ready() && watch.ElapsedMilliseconds < timeout)
        {
            poll();
            Thread.Sleep(5);
        }
        Check(ready(), "LAN discovery operation timed out");
    }
}
