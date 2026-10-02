using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FrogSmashers.Network;

internal sealed class DatagramReliability
{
    private const uint Magic = 0x52435346;
    private const int HeaderSize = 32;
    private const int DatagramHeaderSize = 21;
    private const int FragmentSize = 1200;
    private const int MaximumMessageSize = 8192;
    private const int MaximumPeers = 32;
    private const int WindowSize = 64;
    private const int QueueLimit = 512;
    private const int RetryMilliseconds = 100;
    private const int TimeoutMilliseconds = 15000;
    private readonly Action<string, byte[]> transmit;
    private readonly Func<long> clock;
    private readonly Dictionary<string, Peer> peers = new();
    private readonly Queue<WireMessage> incoming = new();
    private readonly Queue<string> disconnected = new();
    private HashSet<string> approved = new();
    private long sent;
    private long received;
    private long retried;
    private long ignored;
    private long backpressure;

    public WireStatistics Statistics => new(sent, received, retried, ignored, backpressure);

    public DatagramReliability(Action<string, byte[]> transmit, Func<long> clock)
    {
        this.transmit = transmit;
        this.clock = clock;
    }

    public void SetPeers(IReadOnlyCollection<string> addresses)
    {
        var next = addresses.ToHashSet();
        foreach (string removed in approved.Except(next))
            peers.Remove(removed);
        approved = next;
    }

    public bool IsConnected(string address) => peers.TryGetValue(address, out var peer) && peer.RemoteNonce != 0;

    public bool Receive(out WireMessage message) => incoming.TryDequeue(out message);

    public bool TakeDisconnected(out string address) => disconnected.TryDequeue(out address!);

    public void Send(string address, byte[] data, bool reliable)
    {
        if (data.Length is < 1 or > MaximumMessageSize)
            throw new ArgumentOutOfRangeException(nameof(data));
        var peer = GetPeer(address);
        if (peer == null)
            return;
        peer.LastActivity = clock();
        if (!reliable)
        {
            if (peer.RemoteNonce != 0 && data.Length <= FragmentSize)
            {
                byte[] packet = new byte[DatagramHeaderSize + data.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(packet, Magic);
                packet[4] = 4;
                BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(5), peer.LocalNonce);
                BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(13), peer.RemoteNonce);
                data.CopyTo(packet, DatagramHeaderSize);
                transmit(address, packet);
                sent++;
                return;
            }
            SendFragments(address, peer, 1, peer.NextUnreliable++, data, 0);
            return;
        }

        if (peer.Pending.Count >= QueueLimit)
        {
            backpressure++;
            Fail(address);
            return;
        }

        peer.Pending.Add(peer.NextReliable++, new Pending(data.ToArray(), clock()));
        Flush(address, peer);
    }

    public void Poll()
    {
        long now = clock();
        foreach (var (address, peer) in peers.ToArray())
        {
            if (peer.Pending.Values.Any(pending => now - pending.Created >= TimeoutMilliseconds))
            {
                Fail(address);
                continue;
            }

            if (!approved.Contains(address) && now - peer.LastActivity >= TimeoutMilliseconds)
            {
                peers.Remove(address);
                continue;
            }

            foreach (
                uint sequence in peer
                    .Unreliable.Where(pair => now - pair.Value.Created >= 1000)
                    .Select(pair => pair.Key)
                    .ToArray()
            )
                peer.Unreliable.Remove(sequence);
            Flush(address, peer);
        }
    }

    public void Process(string address, ReadOnlySpan<byte> packet)
    {
        if (packet.Length >= 5 && BinaryPrimitives.ReadUInt32LittleEndian(packet) == Magic && packet[4] == 4)
        {
            ProcessDatagram(address, packet);
            return;
        }
        if (packet.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(packet) != Magic)
        {
            ignored++;
            return;
        }

        byte kind = packet[4];
        int fragment = packet[5];
        int count = packet[6];
        ulong sender = BinaryPrimitives.ReadUInt64LittleEndian(packet[8..]);
        ulong recipient = BinaryPrimitives.ReadUInt64LittleEndian(packet[16..]);
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet[24..]);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(packet[28..]);
        ushort acknowledged = BinaryPrimitives.ReadUInt16LittleEndian(packet[30..]);
        if (
            kind is < 1 or > 3
            || sender == 0
            || sequence == 0
            || (kind == 3 && (packet.Length != HeaderSize || recipient == 0))
            || (
                kind != 3
                && (
                    length is < 1 or > MaximumMessageSize
                    || count != (length + FragmentSize - 1) / FragmentSize
                    || fragment >= count
                    || packet.Length != HeaderSize + Math.Min(FragmentSize, length - fragment * FragmentSize)
                )
            )
        )
        {
            ignored++;
            return;
        }

        peers.TryGetValue(address, out var peer);
        if (recipient != 0 && (peer == null || recipient != peer.LocalNonce))
        {
            ignored++;
            return;
        }
        peer ??= GetPeer(address);
        if (peer == null || peer.RetiredNonces.Contains(sender))
        {
            ignored++;
            return;
        }

        if (peer.RemoteNonce != 0 && peer.RemoteNonce != sender)
        {
            peer.RetiredNonces.Enqueue(peer.RemoteNonce);
            if (peer.RetiredNonces.Count > 8)
                peer.RetiredNonces.Dequeue();
            peer.Pending.Clear();
            peer.Reliable.Clear();
            peer.Unreliable.Clear();
            peer.NextReliable = 1;
            peer.ExpectedReliable = 1;
            disconnected.Enqueue(address);
        }

        peer.RemoteNonce = sender;
        peer.LastActivity = clock();
        received++;
        if (recipient == 0)
        {
            Acknowledge(address, peer, sequence, 0);
            return;
        }
        if (kind == 3)
        {
            if (peer.Pending.TryGetValue(sequence, out var pending))
            {
                pending.Acknowledged |= (ushort)(acknowledged & CompleteMask(pending.Data.Length));
                if (pending.Acknowledged == CompleteMask(pending.Data.Length))
                    peer.Pending.Remove(sequence);
            }
            Flush(address, peer);
            return;
        }

        if (kind == 2 && sequence < peer.ExpectedReliable)
        {
            Acknowledge(address, peer, sequence, CompleteMask(length));
            return;
        }

        if (kind == 2 && sequence - peer.ExpectedReliable >= WindowSize)
        {
            ignored++;
            return;
        }

        var assemblies = kind == 2 ? peer.Reliable : peer.Unreliable;
        if (!assemblies.TryGetValue(sequence, out var assembly))
        {
            if (assemblies.Count >= WindowSize)
            {
                ignored++;
                return;
            }
            assembly = new Assembly(length, clock());
            assemblies.Add(sequence, assembly);
        }

        if (assembly.Data.Length != length)
        {
            ignored++;
            return;
        }
        packet[HeaderSize..].CopyTo(assembly.Data.AsSpan(fragment * FragmentSize));
        assembly.Fragments |= (ushort)(1 << fragment);
        if (kind == 2)
        {
            Acknowledge(address, peer, sequence, assembly.Fragments);
            while (peer.Reliable.TryGetValue(peer.ExpectedReliable, out var next) && next.Complete)
            {
                if (incoming.Count >= 4096)
                {
                    backpressure++;
                    Fail(address);
                    return;
                }
                incoming.Enqueue(new(address, next.Data));
                peer.Reliable.Remove(peer.ExpectedReliable++);
            }
        }
        else if (assembly.Complete)
        {
            if (incoming.Count < 4096)
                incoming.Enqueue(new(address, assembly.Data));
            else
                ignored++;
            assemblies.Remove(sequence);
        }
    }

    private void ProcessDatagram(string address, ReadOnlySpan<byte> packet)
    {
        if (
            packet.Length <= DatagramHeaderSize
            || packet.Length > DatagramHeaderSize + FragmentSize
            || !peers.TryGetValue(address, out var peer)
            || peer.RemoteNonce == 0
            || BinaryPrimitives.ReadUInt64LittleEndian(packet[5..]) != peer.RemoteNonce
            || BinaryPrimitives.ReadUInt64LittleEndian(packet[13..]) != peer.LocalNonce
            || incoming.Count >= 4096
        )
        {
            ignored++;
            return;
        }
        peer.LastActivity = clock();
        received++;
        incoming.Enqueue(new(address, packet[DatagramHeaderSize..].ToArray()));
    }

    private Peer? GetPeer(string address)
    {
        if (peers.TryGetValue(address, out var peer))
            return peer;
        if (peers.Count >= MaximumPeers)
        {
            ignored++;
            return null;
        }
        peer = new Peer { LastActivity = clock() };
        peers.Add(address, peer);
        return peer;
    }

    private void Flush(string address, Peer peer)
    {
        long now = clock();
        if (peer.Pending.Count == 0)
            return;
        uint first = peer.Pending.First().Key;
        foreach (var (sequence, pending) in peer.Pending)
        {
            if (sequence - first >= WindowSize)
                break;
            if (pending.LastSent.HasValue && now - pending.LastSent.Value < RetryMilliseconds)
                continue;
            if (pending.LastSent.HasValue)
                retried++;
            pending.LastSent = now;
            SendFragments(address, peer, 2, sequence, pending.Data, pending.Acknowledged);
        }
    }

    private void SendFragments(string address, Peer peer, byte kind, uint sequence, byte[] data, ushort acknowledged)
    {
        int count = (data.Length + FragmentSize - 1) / FragmentSize;
        for (int fragment = 0; fragment < count; fragment++)
        {
            if ((acknowledged & (1 << fragment)) != 0)
                continue;
            int length = Math.Min(FragmentSize, data.Length - fragment * FragmentSize);
            byte[] packet = Header(kind, peer, sequence, data.Length, count, fragment, 0, length);
            data.AsSpan(fragment * FragmentSize, length).CopyTo(packet.AsSpan(HeaderSize));
            transmit(address, packet);
            sent++;
        }
    }

    private void Acknowledge(string address, Peer peer, uint sequence, ushort fragments)
    {
        transmit(address, Header(3, peer, sequence, 0, 0, 0, fragments, 0));
        sent++;
    }

    private static byte[] Header(
        byte kind,
        Peer peer,
        uint sequence,
        int length,
        int count,
        int fragment,
        ushort acknowledged,
        int payloadLength
    )
    {
        var packet = new byte[HeaderSize + payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, Magic);
        packet[4] = kind;
        packet[5] = (byte)fragment;
        packet[6] = (byte)count;
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(8), peer.LocalNonce);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(16), peer.RemoteNonce);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(24), sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(28), (ushort)length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(30), acknowledged);
        return packet;
    }

    private static ushort CompleteMask(int length) => (ushort)((1 << ((length + FragmentSize - 1) / FragmentSize)) - 1);

    private void Fail(string address)
    {
        if (peers.Remove(address))
            disconnected.Enqueue(address);
    }

    private sealed class Peer
    {
        public readonly ulong LocalNonce =
            BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) | 1;
        public ulong RemoteNonce;
        public long LastActivity;
        public uint NextReliable = 1;
        public uint NextUnreliable = 1;
        public uint ExpectedReliable = 1;
        public readonly Queue<ulong> RetiredNonces = new();
        public readonly SortedDictionary<uint, Pending> Pending = new();
        public readonly Dictionary<uint, Assembly> Reliable = new();
        public readonly Dictionary<uint, Assembly> Unreliable = new();
    }

    private sealed class Pending(byte[] data, long created)
    {
        public readonly byte[] Data = data;
        public readonly long Created = created;
        public long? LastSent;
        public ushort Acknowledged;
    }

    private sealed class Assembly(int length, long created)
    {
        public readonly byte[] Data = new byte[length];
        public readonly long Created = created;
        public ushort Fragments;
        public bool Complete => Fragments == CompleteMask(Data.Length);
    }
}
