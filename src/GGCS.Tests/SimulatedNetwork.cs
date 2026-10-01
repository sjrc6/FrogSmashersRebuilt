using System.Buffers.Binary;

namespace GGCS.Tests;

internal sealed class TestClock : IClock
{
    public long NowMilliseconds { get; set; }
}

internal sealed class SimulatedNetwork(TestClock clock, int seed = 17)
{
    private readonly Random random = new(seed);
    private readonly PriorityQueue<(int From, int To, byte[] Data), (long Time, long Order)> pending = new();
    private readonly Dictionary<int, Queue<Datagram>> inboxes = new();
    private long order;
    public int Latency { get; set; }
    public int Jitter { get; set; }
    public double Loss { get; set; }
    public double Duplication { get; set; }
    public Func<int, int, long, bool>? Block { get; set; }
    public Func<int, int, int>? LinkLatency { get; set; }
    public long Packets { get; private set; }
    public long Bytes { get; private set; }
    public HashSet<(int From, int To)> Links { get; } = new();
    public List<(int From, int To, byte[] Data)> Capture { get; } = new();
    public bool CapturePackets { get; set; }

    public ITransport Endpoint(int id)
    {
        if (!inboxes.ContainsKey(id))
            inboxes.Add(id, new());
        return new EndpointTransport(this, id);
    }

    public void Inject(int from, int to, byte[] packet) => inboxes[to].Enqueue(new(from, packet.ToArray()));

    private void Send(int from, int to, ReadOnlySpan<byte> data)
    {
        Packets++;
        Bytes += data.Length;
        Links.Add((from, to));
        if (CapturePackets)
            Capture.Add((from, to, data.ToArray()));
        if (Block?.Invoke(from, to, clock.NowMilliseconds) == true || random.NextDouble() < Loss)
            return;
        int copies = random.NextDouble() < Duplication ? 2 : 1;
        for (int copy = 0; copy < copies; copy++)
        {
            int delay = Math.Max(0, (LinkLatency?.Invoke(from, to) ?? Latency) + random.Next(-Jitter, Jitter + 1));
            pending.Enqueue((from, to, data.ToArray()), (clock.NowMilliseconds + delay, order++));
        }
    }

    private bool Receive(int id, out Datagram datagram)
    {
        while (pending.TryPeek(out _, out var priority) && priority.Time <= clock.NowMilliseconds)
        {
            var packet = pending.Dequeue();
            if (inboxes.TryGetValue(packet.To, out var inbox))
                inbox.Enqueue(new(packet.From, packet.Data));
        }
        return inboxes[id].TryDequeue(out datagram);
    }

    private sealed class EndpointTransport(SimulatedNetwork network, int id) : ITransport
    {
        public void Send(int peerId, ReadOnlySpan<byte> packet) => network.Send(id, peerId, packet);

        public bool TryReceive(out Datagram datagram) => network.Receive(id, out datagram);
    }
}

internal sealed class IntCodec : IInputCodec<int>
{
    public int Size => 4;

    public void Encode(int input, Span<byte> destination) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, input);

    public int Decode(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadInt32LittleEndian(source);
}

internal sealed class TestGame : IRollbackGame<int, ulong>
{
    public ulong Value { get; set; } = 1469598103934665603;
    public Dictionary<int, ulong> States { get; } = new();
    public int Bias { get; set; }
    public bool BrokenRestore { get; set; }
    public int ReplayedFrames { get; private set; }

    public SavedState<ulong> SaveState() => new(Value, Value);

    public void LoadState(ulong state)
    {
        Value = BrokenRestore ? state + 1 : state;
    }

    public void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<int>> inputs, bool isResimulation)
    {
        if (isResimulation)
            ReplayedFrames++;
        unchecked
        {
            Value = (Value ^ (uint)(frame + Bias)) * 1099511628211;
            for (int i = 0; i < inputs.Length; i++)
                Value =
                    (
                        Value
                        ^ (uint)(inputs[i].Input * (i + 1) + (inputs[i].Status == InputStatus.Disconnected ? 7919 : 0))
                    ) * 1099511628211;
        }
        States[frame + 1] = Value;
    }
}

internal sealed class SessionRig
{
    internal sealed class Node(int id, TestGame game, P2PSession<int, ulong> session)
    {
        public int Id { get; } = id;
        public TestGame Game { get; } = game;
        public P2PSession<int, ulong> Session { get; } = session;
        public double RenderHz { get; set; } = 60;
        public double ClockRate { get; set; } = 1;
        public double NextRender { get; set; }
        public long LastUpdate { get; set; }
        public double Accumulator { get; set; }
        public List<SessionEvent> Events { get; } = new();
    }

    public TestClock Clock { get; } = new();
    public SimulatedNetwork Network { get; }
    public SessionOptions Options { get; }
    public Player[] Players { get; }
    public List<Node> Nodes { get; } = new();
    public List<(int Id, TestGame Game, SpectatorSession<int, ulong> Session)> Spectators { get; } = new();
    public int StopAtFrame { get; set; } = int.MaxValue;
    public bool Pace { get; set; } = true;
    public Func<int, long, bool>? PauseNode { get; set; }
    public int MinimumFrame => Nodes.Min(n => n.Session.CurrentFrame);

    public SessionRig(int[] owners, SessionOptions? options = null, int seed = 17, ulong sessionId = 1)
    {
        Network = new(Clock, seed);
        Options =
            options
            ?? new()
            {
                FramesPerSecond = 120,
                MaxPredictionFrames = 24,
                HistoryFrames = 360,
                InputDelay = 2,
            };
        Players = owners.Select((owner, index) => new Player(index, owner)).ToArray();
        foreach (int owner in owners.Distinct())
            AddNode(owner, sessionId);
    }

    public Node AddNode(int id, ulong sessionId = 1)
    {
        var game = new TestGame();
        var session = new P2PSession<int, ulong>(
            sessionId,
            id,
            Players,
            game,
            new IntCodec(),
            Network.Endpoint(id),
            Options,
            Clock
        );
        var node = new Node(id, game, session);
        Nodes.Add(node);
        return node;
    }

    public void AddSpectator(int id, int host, int startFrame = 0, ulong? checkpoint = null)
    {
        var hostNode = Nodes.Single(n => n.Id == host);
        hostNode.Session.AddSpectator(id, startFrame);
        var game = new TestGame();
        if (checkpoint.HasValue)
            game.LoadState(checkpoint.Value);
        var session = new SpectatorSession<int, ulong>(
            1,
            host,
            Players,
            game,
            new IntCodec(),
            Network.Endpoint(id),
            Options,
            Clock,
            startFrame
        );
        Spectators.Add((id, game, session));
    }

    public static int Input(int frame, int player) => ((frame / (3 + player) + player * 13) % 17) - 8;

    public void Run(int milliseconds, bool advanceSpectators = true)
    {
        long end = Clock.NowMilliseconds + milliseconds;
        while (Clock.NowMilliseconds < end)
        {
            foreach (var node in Nodes)
            {
                if (
                    Clock.NowMilliseconds < node.NextRender
                    || PauseNode?.Invoke(node.Id, Clock.NowMilliseconds) == true
                )
                    continue;
                node.NextRender = Clock.NowMilliseconds + 1000 / node.RenderHz;
                node.Session.Poll();
                double elapsed = (Clock.NowMilliseconds - node.LastUpdate) * node.ClockRate;
                node.LastUpdate = Clock.NowMilliseconds;
                if (node.Session.State == SessionState.Running)
                {
                    node.Accumulator = Math.Min(node.Accumulator + elapsed, 100);
                    int budget = 12;
                    double duration =
                        1000d / Options.FramesPerSecond * (Pace ? node.Session.RecommendedFrameDurationMultiplier : 1);
                    while (node.Accumulator >= duration && budget-- > 0 && node.Session.CurrentFrame < StopAtFrame)
                    {
                        var inputs = node
                            .Session.LocalPlayerHandles.Select(p => Input(node.Session.CurrentFrame, p))
                            .ToArray();
                        var result = node.Session.AdvanceFrame(inputs);
                        if (result != AdvanceStatus.Advanced)
                        {
                            node.Accumulator = Math.Min(node.Accumulator, duration);
                            break;
                        }
                        node.Accumulator -= duration;
                    }
                }
                else
                    node.Accumulator = 0;
                while (node.Session.TryGetEvent(out var item))
                    node.Events.Add(item);
            }
            foreach (var spectator in Spectators)
            {
                if (advanceSpectators)
                    spectator.Session.AdvanceAvailable();
                else
                    spectator.Session.Poll();
            }
            Clock.NowMilliseconds++;
        }
    }

    public void CheckAgreement(string label)
    {
        int frame = Nodes.Min(n => n.Session.ConfirmedFrame) + 1;
        Check.True(frame > 0, $"{label}: has committed frames");
        ulong expected = Nodes[0].Game.States[frame];
        foreach (var node in Nodes)
        {
            Check.Equal(expected, node.Game.States[frame], $"{label}: peer {node.Id} state {frame}");
            Check.True(
                !node.Events.Any(e => e.Kind is SessionEventKind.DesyncDetected or SessionEventKind.ProtocolError),
                $"{label}: peer {node.Id} unexpected failure: {string.Join(", ", node.Events.Where(e => e.Kind is SessionEventKind.DesyncDetected or SessionEventKind.ProtocolError))}"
            );
        }
    }
}
