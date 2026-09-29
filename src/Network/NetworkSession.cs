using FrogSmashers.Core;

namespace FrogSmashers.Network;

public sealed class NetworkSession : IDisposable
{
    private const uint Magic = 0x46535242;
    private const int Protocol = 2;
    private const int BatchSize = 24;
    private readonly SessionConfig config;
    private readonly IPeerTransport transport;
    private readonly SortedDictionary<long, InputFrame>[] receivedInputs;
    private readonly Dictionary<long, InputFrame[]> simulatedInputs = new();
    private readonly SortedDictionary<long, byte[]> snapshots = new();
    private readonly Dictionary<long, ulong> hashes = new();
    private readonly SortedDictionary<long, SimulationEvent[]> eventJournal = new();
    private readonly Dictionary<(int Peer, long Frame), ulong> remoteHashes = new();
    private readonly long[] lastReceivedFrames;
    private readonly long[] lastAcknowledgedFrames;
    private readonly long[] remoteSimulationTicks;
    private readonly long[] remoteConfirmedFrames;
    private readonly long[] verifiedStateFrames;
    private readonly int[] estimatedOneWayTicks;
    private long rollbackFrame = long.MaxValue;
    private long lastBroadcastMilliseconds = long.MinValue;
    public World World { get; }
    public byte[] PreviousSnapshot { get; private set; }
    public string? Error { get; private set; }
    public bool IsTransportFailure { get; private set; }
    public long ConfirmedFrame => Math.Min(World.TickNumber - 1, lastReceivedFrames.Min());
    public long RollbackCount { get; private set; }
    public long LastRollbackFromFrame { get; private set; } = -1;

    public IEnumerable<SimulationEvent> EventsSince(long frame) =>
        eventJournal.Where(p => p.Key >= frame).SelectMany(p => p.Value);

    public long ResimulatedTicks { get; private set; }
    public int RejectedPackets { get; private set; }
    public int LocalPeer => config.LocalPeer;
    public int[] LocalSlots => config.PeerSlots[config.LocalPeer];

    public long MinimumRemoteTick
    {
        get
        {
            long minimum = long.MaxValue;
            for (int peer = 0; peer < remoteSimulationTicks.Length; peer++)
            {
                if (peer != LocalPeer)
                {
                    minimum = Math.Min(minimum, remoteSimulationTicks[peer]);
                }
            }

            return minimum == long.MaxValue ? World.TickNumber : minimum;
        }
    }

    public int FramesAheadOfPeers
    {
        get
        {
            long lead = 0;
            for (int peer = 0; peer < remoteSimulationTicks.Length; peer++)
            {
                if (peer != LocalPeer)
                {
                    lead = Math.Max(lead, World.TickNumber - remoteSimulationTicks[peer] - estimatedOneWayTicks[peer]);
                }
            }

            return (int)lead;
        }
    }

    public bool ShouldWaitForPeers => FramesAheadOfPeers > 3;

    public bool AllPeersConfirmed(long frame)
    {
        if (Error != null || ConfirmedFrame < frame)
        {
            return false;
        }

        for (int peer = 0; peer < remoteConfirmedFrames.Length; peer++)
        {
            if (peer != LocalPeer && (remoteConfirmedFrames[peer] < frame || verifiedStateFrames[peer] < frame + 1))
            {
                return false;
            }
        }

        return true;
    }

    public int PredictionDepth => (int)Math.Max(0, World.TickNumber - 1 - ConfirmedFrame);

    public NetworkSession(World world, SessionConfig config, IPeerTransport transport)
    {
        World = world;
        this.config = config;
        this.transport = transport;
        if (world.TickNumber != 0 || world.Players.Length != config.PlayerCount)
        {
            throw new ArgumentException("Session needs matching fresh world");
        }

        receivedInputs = Enumerable
            .Range(0, config.PlayerCount)
            .Select(_ => new SortedDictionary<long, InputFrame>())
            .ToArray();
        lastReceivedFrames = Enumerable.Repeat((long)config.InputDelay - 1, config.PeerSlots.Length).ToArray();
        remoteSimulationTicks = new long[config.PeerSlots.Length];
        remoteConfirmedFrames = Enumerable.Repeat(-1L, config.PeerSlots.Length).ToArray();
        verifiedStateFrames = new long[config.PeerSlots.Length];
        estimatedOneWayTicks = new int[config.PeerSlots.Length];
        lastAcknowledgedFrames = Enumerable.Repeat((long)config.InputDelay - 1, config.PeerSlots.Length).ToArray();
        for (int slot = 0; slot < receivedInputs.Length; slot++)
        {
            for (long frame = 0; frame < config.InputDelay; frame++)
            {
                receivedInputs[slot][frame] = default;
            }
        }

        PreviousSnapshot = world.Capture();
        snapshots[0] = PreviousSnapshot;
        hashes[0] = World.Hash(PreviousSnapshot);
    }

    public void Poll()
    {
        if (Error != null)
        {
            return;
        }

        transport.Poll();
        if (transport.Error != null)
        {
            Error = transport.Error;
            IsTransportFailure = true;
            return;
        }

        int count = 0;
        while (count++ < 512 && transport.TryReceive(out var packet))
        {
            Receive(packet.Peer, packet.Data);
        }

        Repair();
        CheckHashes();
        Broadcast(false);
    }

    public bool TryAdvance(InputFrame[] localInputs)
    {
        if (localInputs.Length != LocalSlots.Length)
        {
            throw new ArgumentException("Pass inputs in LocalSlots order");
        }

        if (Error != null || PredictionDepth >= config.MaxPrediction)
        {
            return false;
        }

        foreach (var input in localInputs)
        {
            _ = InputFrame.FromPacked(input.Packed);
        }

        var captureFrame = World.TickNumber + config.InputDelay;
        for (int n = 0; n < LocalSlots.Length; n++)
        {
            receivedInputs[LocalSlots[n]].TryAdd(captureFrame, localInputs[n]);
        }

        AdvanceContiguous(config.LocalPeer);
        Broadcast();
        Step();
        Prune();
        CheckHashes();
        return true;
    }

    private void Step()
    {
        long frame = World.TickNumber;
        var inputs = new InputFrame[receivedInputs.Length];
        for (int slot = 0; slot < inputs.Length; slot++)
        {
            if (!receivedInputs[slot].TryGetValue(frame, out inputs[slot]))
            {
                inputs[slot] =
                    frame > 0 && simulatedInputs.TryGetValue(frame - 1, out var prior) ? prior[slot] : default;
            }
        }

        simulatedInputs[frame] = inputs;
        PreviousSnapshot = snapshots[frame];
        World.Tick(inputs);
        eventJournal[frame] = World.Events.ToArray();
        snapshots[World.TickNumber] = World.Capture();
        hashes[World.TickNumber] = World.Hash(snapshots[World.TickNumber]);
    }

    private void Repair()
    {
        if (rollbackFrame == long.MaxValue || Error != null)
        {
            return;
        }

        long target = World.TickNumber;
        long rewind = rollbackFrame;
        rollbackFrame = long.MaxValue;
        if (!snapshots.TryGetValue(rewind, out var snapshot))
        {
            Error = "Late input exceeded retained rollback history";
            return;
        }

        World.Restore(snapshot);
        RollbackCount++;
        LastRollbackFromFrame = rewind;
        while (World.TickNumber < target)
        {
            Step();
            ResimulatedTicks++;
        }
    }

    private void AdvanceContiguous(int peer)
    {
        while (HasInputsForEverySlot(peer, lastReceivedFrames[peer] + 1))
        {
            lastReceivedFrames[peer]++;
        }
    }

    private bool HasInputsForEverySlot(int peer, long frame)
    {
        foreach (int slot in config.PeerSlots[peer])
        {
            if (!receivedInputs[slot].ContainsKey(frame))
            {
                return false;
            }
        }

        return true;
    }

    private void Broadcast(bool newInput = true)
    {
        if (Error != null)
        {
            return;
        }

        long now = transport.TimeMilliseconds;
        if (!newInput && lastBroadcastMilliseconds != long.MinValue && now - lastBroadcastMilliseconds < 20)
        {
            return;
        }

        lastBroadcastMilliseconds = now;
        for (int peer = 0; peer < config.PeerSlots.Length; peer++)
        {
            if (peer == config.LocalPeer)
            {
                continue;
            }

            long first = lastAcknowledgedFrames[peer] + 1;
            long last = lastReceivedFrames[config.LocalPeer];
            if (first > last)
            {
                first = Math.Max(config.InputDelay, last);
            }

            int count = (int)Math.Clamp(last - first + 1, 0, BatchSize);
            using var stream = new MemoryStream(1024);
            using var writer = new BinaryWriter(stream);
            writer.Write(Magic);
            writer.Write((byte)Protocol);
            writer.Write(config.Fingerprint);
            writer.Write((byte)config.LocalPeer);
            long hashFrame = ConfirmedFrame + 1;
            var header = new InputPacketHeader(
                World.TickNumber,
                lastReceivedFrames[peer],
                ConfirmedFrame,
                hashFrame,
                hashes.GetValueOrDefault(hashFrame),
                first,
                (byte)count
            );
            header.Write(writer);
            for (int n = 0; n < count; n++)
            {
                foreach (int slot in LocalSlots)
                {
                    writer.Write(receivedInputs[slot][first + n].Packed);
                }
            }

            transport.Send(peer, stream.ToArray());
        }
    }

    private void Receive(int peer, byte[] data)
    {
        if (peer < 0 || peer >= config.PeerSlots.Length || peer == config.LocalPeer || data.Length is < 87 or > 1200)
        {
            RejectedPackets++;
            return;
        }

        try
        {
            using var reader = new BinaryReader(new MemoryStream(data, false));
            if (reader.ReadUInt32() != Magic || reader.ReadByte() != Protocol)
            {
                RejectedPackets++;
                return;
            }

            if (!reader.ReadBytes(32).SequenceEqual(config.Fingerprint))
            {
                Error = "Peer build, content, rules or roster does not match";
                return;
            }

            if (reader.ReadByte() != peer)
            {
                RejectedPackets++;
                return;
            }

            var header = InputPacketHeader.Read(reader);
            int slotCount = config.PeerSlots[peer].Length;
            long payloadBytes = reader.BaseStream.Length - reader.BaseStream.Position;
            if (!IsValidInputWindow(header, slotCount, payloadBytes))
            {
                RejectedPackets++;
                return;
            }
            var inputs = ReadInputs(reader, header.InputCount, slotCount);
            long floor = Math.Max(0, World.TickNumber - config.HistoryFrames + 1);
            lastAcknowledgedFrames[peer] = Math.Max(lastAcknowledgedFrames[peer], header.LastReceivedFrame);
            remoteConfirmedFrames[peer] = Math.Max(remoteConfirmedFrames[peer], header.ConfirmedFrame);
            if (header.Tick >= remoteSimulationTicks[peer])
            {
                remoteSimulationTicks[peer] = header.Tick;
                estimatedOneWayTicks[peer] = (int)
                    Math.Clamp(
                        (World.TickNumber + config.InputDelay - header.LastReceivedFrame) / 2,
                        0,
                        config.MaxPrediction
                    );
            }

            if (header.HashFrame >= floor && header.HashFrame > 0)
            {
                remoteHashes[(peer, header.HashFrame)] = header.Hash;
            }

            for (int n = 0; n < header.InputCount; n++)
            {
                long frame = header.FirstInputFrame + n;
                if (frame < floor)
                {
                    continue;
                }

                for (int p = 0; p < config.PeerSlots[peer].Length; p++)
                {
                    int slot = config.PeerSlots[peer][p];
                    var input = inputs[n, p];
                    if (receivedInputs[slot].TryGetValue(frame, out var old) && old != input)
                    {
                        Error = "Peer changed an already submitted input";
                        return;
                    }

                    receivedInputs[slot][frame] = input;
                    if (simulatedInputs.TryGetValue(frame, out var predicted) && predicted[slot] != input)
                    {
                        rollbackFrame = Math.Min(rollbackFrame, frame);
                    }
                }
            }

            AdvanceContiguous(peer);
        }
        catch (EndOfStreamException)
        {
            RejectedPackets++;
        }
        catch (InvalidDataException)
        {
            RejectedPackets++;
        }
    }

    private bool IsValidInputWindow(InputPacketHeader header, int slotCount, long payloadBytes)
    {
        long latestFrame = World.TickNumber + config.HistoryFrames;
        if (
            header.Tick < 0
            || header.Tick > latestFrame
            || header.LastReceivedFrame < -1
            || header.LastReceivedFrame > lastReceivedFrames[LocalPeer]
        )
        {
            return false;
        }
        if (
            header.ConfirmedFrame < -1
            || header.ConfirmedFrame >= header.Tick
            || header.HashFrame != header.ConfirmedFrame + 1
            || header.HashFrame < 0
            || header.HashFrame > header.Tick
        )
        {
            return false;
        }
        if (
            header.FirstInputFrame < 0
            || header.FirstInputFrame > latestFrame
            || header.InputCount > BatchSize
            || header.FirstInputFrame + header.InputCount > latestFrame
        )
        {
            return false;
        }
        return payloadBytes == header.InputCount * slotCount * sizeof(uint);
    }

    private static InputFrame[,] ReadInputs(BinaryReader reader, int frameCount, int slotCount)
    {
        var inputs = new InputFrame[frameCount, slotCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            for (int slot = 0; slot < slotCount; slot++)
            {
                inputs[frame, slot] = InputFrame.FromPacked(reader.ReadUInt32());
            }
        }
        return inputs;
    }

    private void CheckHashes()
    {
        foreach (var pair in remoteHashes.ToArray())
        {
            if (pair.Key.Frame > ConfirmedFrame + 1)
            {
                continue;
            }

            if (hashes.TryGetValue(pair.Key.Frame, out var local))
            {
                if (local != pair.Value)
                {
                    Error =
                        $"Desync with peer {pair.Key.Peer} at state {pair.Key.Frame}: {local:x16} != {pair.Value:x16}";
                }
                else
                {
                    verifiedStateFrames[pair.Key.Peer] = Math.Max(verifiedStateFrames[pair.Key.Peer], pair.Key.Frame);
                }
            }

            remoteHashes.Remove(pair.Key);
        }
    }

    private void Prune()
    {
        long floor = Math.Max(0, World.TickNumber - config.HistoryFrames + 1);
        foreach (var map in receivedInputs)
        {
            foreach (long k in map.Keys.TakeWhile(k => k < floor).ToArray())
            {
                map.Remove(k);
            }
        }

        foreach (long k in snapshots.Keys.TakeWhile(k => k < floor).ToArray())
        {
            snapshots.Remove(k);
        }

        foreach (long k in eventJournal.Keys.TakeWhile(k => k < floor).ToArray())
        {
            eventJournal.Remove(k);
        }

        foreach (long k in simulatedInputs.Keys.Where(k => k < floor).ToArray())
        {
            simulatedInputs.Remove(k);
        }

        foreach (long k in hashes.Keys.Where(k => k < floor).ToArray())
        {
            hashes.Remove(k);
        }

        foreach (var k in remoteHashes.Keys.Where(k => k.Frame < floor).ToArray())
        {
            remoteHashes.Remove(k);
        }

        if (lastAcknowledgedFrames.Where((_, p) => p != config.LocalPeer).Any(a => a + 1 < floor))
        {
            Error = "Peer stopped acknowledging inputs; connection cannot safely continue";
        }
    }

    public void Dispose() => transport.Dispose();
}
