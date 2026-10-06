using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class UdpLobbyDepartureTests
{
    public static void Run(bool duringMatch)
    {
        using var reserve = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        reserve.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reserve.LocalEndPoint!).Port;
        reserve.Close();
        var nodes = new List<Node>();
        var timer = Stopwatch.StartNew();
        try
        {
            for (int index = 0; index < 5; index++)
                nodes.Add(
                    new(
                        index == 0
                            ? UdpLobby.Host(port, 8, [new(0, Spawned: true)], "departure", "{}")
                            : UdpLobby.Join("127.0.0.1", port, [new(0, Spawned: true)], "departure")
                    )
                );
            var host = nodes[0];
            bool Ready(int count) =>
                nodes
                    .Where(node => node.Active)
                    .All(node =>
                        node.Lobby.SimulationReady
                        && node.Lobby.Roster.Count == count
                        && node.Simulation.Membership.Count == count
                        && node.Lobby.Error == null
                    );
            void Step()
            {
                foreach (var node in nodes.Where(node => node.Active))
                    node.Step();
                Thread.Sleep(5);
            }
            void Wait(Func<bool> condition, string message)
            {
                long deadline = timer.ElapsedMilliseconds + 10000;
                while (!condition() && timer.ElapsedMilliseconds < deadline)
                    Step();
                Check(condition(), message);
            }
            Wait(() => Ready(5), "Five real UDP clients did not settle");
            for (int i = 0; i < 80; i++)
                Step();
            if (duringMatch)
            {
                Check(host.Lobby.StartMatch(_ => "{}"), "Departure fixture failed to start a match");
                Wait(() => nodes.All(node => node.Match?.World.TickNumber >= 80), "UDP match failed to advance");
            }
            nodes[2].Dispose();
            nodes[2].Active = false;
            long departed = timer.ElapsedMilliseconds;
            Wait(() => Ready(4), "The surviving UDP clients did not return to a settled four-player lobby");
            Console.WriteLine(
                $"Real UDP {(duringMatch ? "match" : "lobby")} departure settled in {timer.ElapsedMilliseconds - departed} ms"
            );
            var ticks = nodes
                .Where(node => node.Active)
                .ToDictionary(node => node, node => node.Simulation.World.TickNumber);
            for (int i = 0; i < 120; i++)
                Step();
            Check(
                nodes.Where(node => node.Active).All(node => node.Simulation.World.TickNumber > ticks[node] + 60),
                "Remaining UDP clients must keep advancing after a guest closes"
            );
            long tick = nodes.Where(node => node.Active).Min(node => node.Lobby.LobbySession!.ConfirmedFrame) + 1;
            byte[]? expected = null;
            foreach (var node in nodes.Where(node => node.Active))
            {
                Check(
                    node.Lobby.LobbySession!.TryGetConfirmedCheckpoint(tick, out var state),
                    "Departure checkpoint unavailable"
                );
                Check(
                    expected == null || expected.SequenceEqual(state),
                    "Surviving UDP clients disagree after departure"
                );
                expected = state;
            }
        }
        finally
        {
            foreach (var node in nodes)
                node.Dispose();
        }
    }

    private sealed class Node : IDisposable
    {
        public UdpLobby Lobby { get; }
        public LobbySimulation Simulation { get; private set; } = null!;
        public NetworkSession? Match { get; private set; }
        public bool Active = true;
        private int generation;

        public Node(UdpLobby lobby)
        {
            Lobby = lobby;
            OpenLobby();
        }

        private void OpenLobby()
        {
            Match?.Dispose();
            Match = null;
            generation = Lobby.Generation;
            Simulation = new(new World(TestFixtures.Map(), new GameRules(lobby: true, playerCount: 8)), Lobby.Roster);
            Lobby.AttachSimulation(Simulation);
        }

        public void Step()
        {
            Lobby.Poll();
            Match?.Poll();
            if (Lobby.Generation != generation)
                OpenLobby();
            if (Lobby.Error != null)
                throw new InvalidOperationException($"Peer {Lobby.LocalPeer}: {Lobby.Error}");
            if (Lobby.Ready && Match == null)
            {
                var world = TestFixtures.MakeWorld(5);
                Match = new(
                    world,
                    new SessionConfig(
                        Lobby.PeerSlots,
                        Lobby.LocalPeer,
                        "departure",
                        world,
                        Lobby.SessionId,
                        activePeers: Lobby.PeerIds.ToArray(),
                        inputPlayerSlots: Lobby.InputPlayerSlots
                    ),
                    Lobby.CreateTransport()
                );
            }
            if (Match != null)
            {
                Match.TryAdvance(new RollbackInput[Match.LocalSlots.Length]);
                if (Match.Error != null)
                    throw new InvalidOperationException($"Match peer {Lobby.LocalPeer}: {Match.Error}");
            }
            else if (Lobby.LobbySession is { } session)
            {
                session.TryAdvance(
                    session
                        .LocalSlots.Select(handle =>
                            Simulation.InputSources[handle].HostCommand
                                ? new RollbackInput(default, Cpu: Lobby.HostCommand)
                                : default
                        )
                        .ToArray()
                );
                if (session.Error != null)
                    throw new InvalidOperationException($"Lobby peer {Lobby.LocalPeer}: {session.Error}");
            }
        }

        public void Dispose()
        {
            Match?.Dispose();
            Lobby.Dispose();
        }
    }
}
