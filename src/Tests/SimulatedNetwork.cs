using FrogSmashers.Network;

namespace FrogSmashers.Tests;

internal sealed class SimulatedNetwork
{
    private readonly PriorityQueue<(int From, int To, byte[] Data), (long Tick, uint Order)> pending = new();
    private readonly Queue<Datagram>[] received;
    private uint random;
    public long Time { get; private set; }
    public int DelayTicks { get; set; }
    public int JitterTicks { get; set; }
    public int LossPercent { get; set; }
    public int DuplicatePercent { get; set; }
    public bool Blackout { get; set; }
    public long PacketsSent { get; private set; }
    public long BytesSent { get; private set; }
    public int MaximumPacketBytes { get; private set; }

    public SimulatedNetwork(
        int peers,
        uint seed = 1,
        int delayTicks = 6,
        int jitterTicks = 4,
        int lossPercent = 10,
        int duplicatePercent = 3
    )
    {
        if (peers is < 2 or > LobbyRoster.MaxPeers)
        {
            throw new ArgumentOutOfRangeException(nameof(peers));
        }

        received = Enumerable.Range(0, peers).Select(_ => new Queue<Datagram>()).ToArray();
        random = seed == 0 ? 1 : seed;
        DelayTicks = delayTicks;
        JitterTicks = jitterTicks;
        LossPercent = lossPercent;
        DuplicatePercent = duplicatePercent;
    }

    private uint Next()
    {
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        return random;
    }

    public IPeerTransport Endpoint(int peer) => new EndpointTransport(this, peer);

    public void Advance(int ticks = 1)
    {
        Time += ticks;
        while (pending.TryPeek(out _, out var priority) && priority.Tick <= Time)
        {
            var message = pending.Dequeue();
            received[message.To].Enqueue(new Datagram(message.From, message.Data));
        }
    }

    public void Inject(int from, int to, byte[] payload) => received[to].Enqueue(new Datagram(from, payload));

    private void Send(int from, int to, ReadOnlySpan<byte> data)
    {
        PacketsSent++;
        BytesSent += data.Length;
        MaximumPacketBytes = Math.Max(MaximumPacketBytes, data.Length);
        if (Blackout || Next() % 100 < LossPercent)
        {
            return;
        }

        for (int n = 0, copies = Next() % 100 < DuplicatePercent ? 2 : 1; n < copies; n++)
        {
            int jitter = JitterTicks <= 0 ? 0 : (int)(Next() % (2 * JitterTicks + 1)) - JitterTicks;
            pending.Enqueue((from, to, data.ToArray()), (Time + Math.Max(1, DelayTicks + jitter), Next()));
        }
    }

    private sealed class EndpointTransport(SimulatedNetwork network, int peer) : IPeerTransport
    {
        public string? Error => null;
        public long TimeMilliseconds => network.Time * 1000 / 120;

        public void Poll() { }

        public void Send(int destination, ReadOnlySpan<byte> data) => network.Send(peer, destination, data);

        public bool TryReceive(out Datagram datagram) => network.received[peer].TryDequeue(out datagram);

        public void Dispose() { }
    }
}
