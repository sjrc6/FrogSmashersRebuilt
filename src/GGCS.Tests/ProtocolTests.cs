using GGCS.Protocol;

namespace GGCS.Tests;

internal static class ProtocolTests
{
    public static void Run()
    {
        CompressionRoundTripsAndRejectsDamage();
        HandshakeChecksConfigurationAndGeneration();
        PendingHistoryArrivesInOrderAfterLossAndReordering();
        MalformedInputCannotAcknowledgeOrChangeStatuses();
        ContradictoryInputsDisconnectWithoutPartialAcceptance();
        ReceiveWindowsAndSendQueuesAreBounded();
        StatusUpdatesWorkWhileTheSimulationIsPaused();
        InterruptionResumesAndTimeoutDisconnects();
        RoundTripMeasurementsSurviveSeveralOutstandingPings();
        SpectatorStreamsWorkWithoutReverseInputs();
        ChecksumsAreRetriedAndContradictionsAreRejected();
        MalformedDatagramsCannotThrowOrKeepAConnectionAlive();
        StructuredPacketAndCompressionFuzzStayWithinBounds();
        DisconnectProgressSurvivesPacketReordering();
        ExclusionIsDifferentFromVoluntaryDeparture();
        TimingMetadataIsBoundedAndOrdered();
        CompressedPackingAndSendCoalescing();
        ChecksumAcknowledgmentSurvivesLoss();
    }

    private static void CompressedPackingAndSendCoalescing()
    {
        var pair = new Pair(inputSize: 198, options: SmallOptions() with { HistoryFrames = 1024 });
        pair.Synchronize();
        pair.ClearPackets();
        for (int frame = 0; frame < 400; frame++)
            pair.A.QueueInput(frame, new byte[198]);
        Check.Equal(0, pair.FromA.Packets.Count, "Queueing a batch does not transmit each partial prefix");
        pair.A.Poll(400, pair.Statuses);
        byte[][] packets = pair
            .FromA.Packets.Where(p => p.Data[PacketCodec.HeaderSize - 1] == (byte)PacketKind.Inputs)
            .Select(p => p.Data)
            .ToArray();
        Check.Equal(2, packets.Length, "Compressible history fills packets up to the decoded-size safety limit");
        Check.True(packets.All(p => p.Length <= pair.Options.MaxPacketBytes), "Compressed packets obey the MTU");
        foreach (byte[] data in packets)
        {
            Check.True(PacketCodec.TryDecode(data, 1, 9, 2, out var packet, out _), "Packed inputs decode");
            Check.True(
                (long)packet.InputCount * 198 <= InputCompression.MaximumDecodedBytes,
                "Packet packing retains a bounded expansion size"
            );
            pair.B.HandlePacket(data);
        }
        Check.Equal(399, pair.B.LastReceivedFrame, "All packed frames arrive despite newest-first packet delivery");
        pair.ClearPackets();
        for (int poll = 0; poll < 100; poll++)
            pair.A.Poll(400, pair.Statuses);
        Check.Equal(0, pair.FromA.Packets.Count, "Repeated polls do not resend an unchanged batch");

        var random = new Random(544);
        byte[][] noise = Enumerable.Range(0, 80).Select(_ => new byte[198]).ToArray();
        foreach (byte[] frame in noise)
            random.NextBytes(frame);
        for (int start = 0; start < noise.Length; )
        {
            byte[] data = InputCompression.EncodePacket(noise, start, 198, 900, out int count);
            Check.True(data.Length <= 900 && count > 0, "Incompressible data still fits the packet budget");
            Check.True(InputCompression.TryDecode(data, count, 198, out var decoded), "Incompressible chunks decode");
            for (int frame = 0; frame < count; frame++)
                Check.True(
                    noise[start + frame].SequenceEqual(decoded[frame]),
                    "Packet splitting preserves noisy inputs"
                );
            start += count;
        }
    }

    private static void ChecksumAcknowledgmentSurvivesLoss()
    {
        var pair = new Pair();
        pair.Synchronize();
        pair.ClearPackets();
        pair.A.QueueChecksum(0, 123);
        pair.B.HandlePacket(pair.FromA.Packets.Single().Data);
        Check.True(pair.B.TryReceiveChecksum(out _), "First checksum reaches its recipient");
        pair.ClearPackets();
        pair.Step(201);
        Check.True(!pair.B.TryReceiveChecksum(out _), "A lost checksum acknowledgment does not duplicate delivery");
        pair.ClearPackets();
        pair.Clock.NowMilliseconds += 201;
        pair.A.Poll(0, pair.Statuses);
        Check.True(
            pair.FromA.Packets.All(p => p.Data[PacketCodec.HeaderSize - 1] != (byte)PacketKind.Checksum),
            "Acknowledged checksums leave the retry queue"
        );
        Check.Throws<InvalidOperationException>(
            () => pair.A.QueueChecksum(0, 124),
            "Acknowledgment does not discard checksum immutability history"
        );
    }

    private static void TimingMetadataIsBoundedAndOrdered()
    {
        var pair = new Pair();
        pair.Synchronize();
        pair.A.SetTiming(
            new()
            {
                DelayFrames = 2,
                DonationFrames = 4,
                MaxExtraDelayFrames = 2,
            },
            1
        );
        pair.Step(250);
        Check.Equal(2, pair.B.Stats.ResponseDelayFrames, "Response delay travels with protocol progress");
        Check.Equal(4, pair.B.Stats.DonationFrames, "Donation travels with protocol progress");
        Check.Equal(1, pair.B.Stats.ExtraDelayFrames, "Temporary delay is reported to remote peers");
        Check.Equal(2, pair.B.Stats.MaxExtraDelayFrames, "Remote automatic-delay limit is visible");
        var stale = new ProtocolPacket
        {
            Kind = PacketKind.Ping,
            Frame = 0,
            Timestamp = pair.Clock.NowMilliseconds,
        };
        pair.B.HandlePacket(PacketCodec.Encode(1, 9, stale));
        Check.Equal(2, pair.B.Stats.ResponseDelayFrames, "Old timing reports cannot overwrite a newer revision");
        long rejected = pair.B.Stats.InvalidPackets;
        stale.TimingRevision = 42;
        stale.ResponseDelay = 5;
        pair.B.HandlePacket(PacketCodec.Encode(1, 9, stale));
        Check.Equal(rejected + 1, pair.B.Stats.InvalidPackets, "Out-of-range remote delay is rejected");
        Check.Equal(2, pair.B.Stats.ResponseDelayFrames, "Rejected timing leaves existing peer settings intact");
    }

    private static void CompressionRoundTripsAndRejectsDamage()
    {
        var random = new Random(931);
        foreach (int inputSize in new[] { 1, 2, 7, 32, 129 })
        {
            foreach (int count in new[] { 1, 2, 17, 64 })
            {
                byte[][] frames = Enumerable.Range(0, count).Select(_ => new byte[inputSize]).ToArray();
                for (int frame = 0; frame < count; frame++)
                {
                    if (frame % 4 == 0)
                        random.NextBytes(frames[frame]);
                    else if (frame > 0)
                        frames[frame - 1].CopyTo(frames[frame], 0);
                }
                byte[] encoded = InputCompression.Encode(frames, 0, count, inputSize);
                Check.True(
                    InputCompression.TryDecode(encoded, count, inputSize, out byte[][] decoded),
                    "Valid compressed inputs decode"
                );
                for (int frame = 0; frame < count; frame++)
                    Check.True(
                        frames[frame].AsSpan().SequenceEqual(decoded[frame]),
                        "Compression preserves every input byte"
                    );
                Check.True(
                    !InputCompression.TryDecode(encoded.AsSpan(0, encoded.Length - 1), count, inputSize, out _),
                    "Truncated compressed input is rejected"
                );
                Check.True(
                    !InputCompression.TryDecode([.. encoded, 128], count, inputSize, out _),
                    "Excess decompressed input is rejected"
                );
            }
        }
        Check.True(!InputCompression.TryDecode([127], 1, 128, out _), "A truncated literal run is rejected");
        Check.True(!InputCompression.TryDecode([255], 1, 1, out _), "An oversized zero run is rejected");
        Check.True(
            !InputCompression.TryDecode([128], int.MaxValue, int.MaxValue, out _),
            "Impossible decompression sizes are rejected"
        );
    }

    private static void HandshakeChecksConfigurationAndGeneration()
    {
        var pair = new Pair();
        pair.Synchronize();
        Check.Equal(SessionState.Running, pair.A.State, "Five handshake round trips synchronize the first peer");
        Check.Equal(SessionState.Running, pair.B.State, "Five handshake round trips synchronize the second peer");
        Check.Equal(
            4,
            DrainEvents(pair.A).Count(e => e.Kind == SessionEventKind.Synchronizing),
            "Handshake reports each incomplete round trip once"
        );

        byte[] stale = PacketCodec.Encode(2, 9, new ProtocolPacket { Kind = PacketKind.Disconnect });
        pair.A.HandlePacket(stale);
        Check.Equal(SessionState.Running, pair.A.State, "Old session packets cannot disconnect a new session");
        Check.Equal(1L, pair.A.Stats.StalePackets, "Stale packet diagnostics count rejected generations");

        byte[] incompatible = PacketCodec.Encode(
            1,
            9,
            new ProtocolPacket
            {
                Kind = PacketKind.Synchronize,
                SendInputSize = 2,
                ReceiveInputSize = 1,
                PlayerCount = 2,
            }
        );
        pair.A.HandlePacket(incompatible);
        Check.Equal(SessionState.Disconnected, pair.A.State, "A peer with a different input layout is rejected");
        Check.True(
            DrainEvents(pair.A).Any(e => e.Kind == SessionEventKind.ProtocolError),
            "Configuration disagreement emits a clear protocol error"
        );
    }

    private static void PendingHistoryArrivesInOrderAfterLossAndReordering()
    {
        var pair = new Pair(inputSize: 48, options: SmallOptions() with { MaxPacketBytes = 256 });
        pair.Synchronize();
        pair.ClearPackets();
        for (int frame = 0; frame < 90; frame++)
            pair.A.QueueInput(frame, Enumerable.Repeat((byte)frame, 48).ToArray());
        pair.ClearPackets();
        pair.Clock.NowMilliseconds += 100;
        pair.A.Poll(90, pair.Statuses);
        byte[][] packets = pair.FromA.Packets.Select(p => p.Data).ToArray();
        Check.True(packets.Length > 1, "Pending history is split to obey the packet limit");
        Check.True(packets.All(packet => packet.Length <= 256), "Every pending-history chunk obeys the MTU");
        pair.B.HandlePacket(packets[0]);
        Check.Equal(
            -1,
            pair.B.LastReceivedFrame,
            "Receiving the newest chunk does not acknowledge a missing earlier frame"
        );
        Check.True(!pair.B.TryReceiveInput(out _), "Out-of-order inputs wait for missing predecessors");
        foreach (byte[] packet in packets.Reverse())
        {
            pair.B.HandlePacket(packet);
            pair.B.HandlePacket(packet);
        }
        Check.Equal(
            89,
            pair.B.LastReceivedFrame,
            "Retransmission repairs the full backlog rather than a fixed oldest batch"
        );
        for (int frame = 0; frame < 90; frame++)
        {
            Check.True(pair.B.TryReceiveInput(out InputPacket input), "Each recovered input is delivered");
            Check.Equal(frame, input.Frame, "Recovered inputs remain in order");
            Check.True(input.Data.All(value => value == frame), "Recovered input data is unchanged");
        }
        Check.True(!pair.B.TryReceiveInput(out _), "Duplicated chunks do not deliver duplicated inputs");
        pair.ClearPackets();
        pair.B.Poll(90, pair.Statuses);
        pair.Deliver();
        Check.Equal(89, pair.A.LastAcknowledgedFrame, "Only the contiguous received prefix is acknowledged");
        Check.Equal(0, pair.A.Stats.PendingInputFrames, "Acknowledged history leaves the send queue");
    }

    private static void MalformedInputCannotAcknowledgeOrChangeStatuses()
    {
        var pair = new Pair();
        pair.Synchronize();
        pair.A.QueueInput(0, [7]);
        ProtocolPacket packet = Inputs(acknowledged: 0, count: 1, bytes: [0]);
        packet.Statuses[1] = new ConnectionStatus(true, -1);
        pair.A.HandlePacket(PacketCodec.Encode(1, 9, packet));
        Check.Equal(-1, pair.A.LastAcknowledgedFrame, "Malformed compression cannot acknowledge pending inputs");
        Check.True(!pair.A.RemoteStatuses[1].Disconnected, "Malformed compression cannot publish a disconnect");
        Check.Equal(-1, pair.A.LastReceivedFrame, "Malformed compression cannot advance the receive stream");

        packet = Inputs(acknowledged: 1, count: 1, bytes: [0, 6]);
        pair.A.HandlePacket(PacketCodec.Encode(1, 9, packet));
        Check.Equal(-1, pair.A.LastAcknowledgedFrame, "An acknowledgment beyond sent history is rejected");
        Check.Equal(-1, pair.A.LastReceivedFrame, "Invalid acknowledgments reject the whole datagram");
    }

    private static void ContradictoryInputsDisconnectWithoutPartialAcceptance()
    {
        var pair = new Pair();
        pair.Synchronize();
        pair.A.HandlePacket(PacketCodec.Encode(1, 9, Inputs(-1, 1, [0, 5])));
        Check.Equal(0, pair.A.LastReceivedFrame, "Initial input is accepted");
        pair.A.HandlePacket(PacketCodec.Encode(1, 9, Inputs(-1, 2, [1, 6, 1])));
        Check.Equal(
            SessionState.Disconnected,
            pair.A.State,
            "Changing an already submitted input disconnects the peer"
        );
        Check.Equal(0, pair.A.LastReceivedFrame, "Contradictory batches do not partially publish new input");
    }

    private static void ReceiveWindowsAndSendQueuesAreBounded()
    {
        var pair = new Pair(options: SmallOptions());
        for (int frame = 0; frame < pair.Options.InputCapacity; frame++)
            pair.A.QueueInput(frame, [(byte)frame]);
        Check.True(!pair.A.CanQueueInput, "Unacknowledged history has a fixed configured bound");
        Check.Throws<InvalidOperationException>(
            () => pair.A.QueueInput(pair.Options.InputCapacity, [1]),
            "A full queue never silently discards inputs"
        );
        pair.Synchronize();
        Check.Equal(
            pair.Options.InputCapacity - 1,
            pair.B.LastReceivedFrame,
            "Inputs queued during synchronization are sent after synchronization"
        );
        while (pair.B.TryReceiveInput(out _)) { }

        ProtocolPacket tooFarAhead = Inputs(-1, 1, [0, 1]);
        tooFarAhead.StartFrame = pair.B.LastReceivedFrame + pair.Options.InputCapacity + 1;
        pair.B.HandlePacket(PacketCodec.Encode(1, 9, tooFarAhead));
        Check.True(!pair.B.TryReceiveInput(out _), "Future input outside the receive window is rejected");
        Check.Equal(
            SessionState.Running,
            pair.B.State,
            "A rejected out-of-window datagram does not break the valid connection"
        );
    }

    private static void StatusUpdatesWorkWhileTheSimulationIsPaused()
    {
        var pair = new Pair();
        pair.Synchronize();
        pair.Statuses[1] = new ConnectionStatus(true, -1);
        pair.A.Poll(0, pair.Statuses);
        pair.Deliver();
        Check.True(
            pair.B.RemoteStatuses[1].Disconnected,
            "Player disconnect information propagates without input frames"
        );
        Check.Equal(-1, pair.B.LastReceivedFrame, "Control-plane status updates do not manufacture player inputs");
    }

    private static void InterruptionResumesAndTimeoutDisconnects()
    {
        var options = SmallOptions() with { DisconnectNotifyMilliseconds = 100, DisconnectTimeoutMilliseconds = 500 };
        var pair = new Pair(options: options);
        pair.Synchronize();
        DrainEvents(pair.A);
        pair.ClearPackets();
        pair.Clock.NowMilliseconds += 110;
        pair.A.Poll(0, pair.Statuses);
        Check.True(
            DrainEvents(pair.A).Any(e => e.Kind == SessionEventKind.NetworkInterrupted),
            "Packet silence reports interruption before disconnecting"
        );
        pair.B.Poll(0, pair.Statuses);
        pair.Deliver();
        Check.True(
            DrainEvents(pair.A).Any(e => e.Kind == SessionEventKind.NetworkResumed),
            "Valid traffic clears the interruption immediately"
        );
        pair.ClearPackets();
        pair.Clock.NowMilliseconds += 501;
        pair.A.Poll(0, pair.Statuses);
        Check.Equal(
            SessionState.Disconnected,
            pair.A.State,
            "The disconnect timeout also applies while no simulation frames advance"
        );
    }

    private static void RoundTripMeasurementsSurviveSeveralOutstandingPings()
    {
        var pair = new Pair(options: SmallOptions() with { QualityReportMilliseconds = 50 }, delayMilliseconds: 250);
        pair.Synchronize();
        for (int step = 0; step < 250; step++)
            pair.Step(10);
        Check.Equal(
            500.0,
            pair.A.Stats.RoundTripMilliseconds,
            "RTT stays measurable when several newer pings are already outstanding"
        );
        Check.Equal(500.0, pair.B.Stats.RoundTripMilliseconds, "The reverse RTT measurement is also correct");
        Check.Equal(SessionState.Running, pair.A.State, "A healthy high-latency link remains connected");
    }

    private static void SpectatorStreamsWorkWithoutReverseInputs()
    {
        var pair = new Pair(inputSize: 4, reverseSize: 0, initialFrame: 120);
        pair.Synchronize();
        pair.A.QueueInput(120, [1, 2, 3, 4]);
        pair.Step(20);
        pair.Step(20);
        Check.True(
            pair.B.TryReceiveInput(out InputPacket input),
            "A spectator receives a combined confirmed-input stream"
        );
        Check.Equal(120, input.Frame, "Spectator streams may begin from a retained checkpoint frame");
        Check.Equal(120, pair.A.LastAcknowledgedFrame, "A spectator acknowledges without sending gameplay input");
        Check.Equal(119, pair.A.LastReceivedFrame, "An observer does not invent a reverse player stream");
        pair.Statuses[1] = new ConnectionStatus(true, 4);
        pair.A.Poll(120, pair.Statuses);
        pair.Deliver();
        Check.Equal(
            new ConnectionStatus(true, 4),
            pair.B.RemoteStatuses[1],
            "A spectator checkpoint retains disconnect cutoffs from earlier player history"
        );
        Check.Throws<ArgumentException>(
            () => pair.B.QueueInput(120, []),
            "Spectators cannot submit an empty gameplay input"
        );
    }

    private static void ChecksumsAreRetriedAndContradictionsAreRejected()
    {
        var pair = new Pair();
        pair.Synchronize();
        pair.A.QueueChecksum(0, 312);
        pair.ClearPackets();
        pair.Step(201);
        Check.True(
            pair.B.TryReceiveChecksum(out ChecksumPacket checksum),
            "Checksums survive loss of their first datagram"
        );
        Check.Equal(new ChecksumPacket(0, 312), checksum, "The retried checksum retains its frame");
        pair.Step(201);
        Check.True(!pair.B.TryReceiveChecksum(out _), "Retried checksums are deduplicated");
        pair.B.HandlePacket(
            PacketCodec.Encode(
                1,
                9,
                new ProtocolPacket
                {
                    Kind = PacketKind.Checksum,
                    Frame = 0,
                    Checksum = 313,
                }
            )
        );
        Check.Equal(SessionState.Disconnected, pair.B.State, "A confirmed checksum is immutable");
    }

    private static void MalformedDatagramsCannotThrowOrKeepAConnectionAlive()
    {
        var pair = new Pair(
            options: SmallOptions() with
            {
                DisconnectNotifyMilliseconds = 100,
                DisconnectTimeoutMilliseconds = 500,
            }
        );
        pair.Synchronize();
        var random = new Random(71);
        for (int iteration = 0; iteration < 2000; iteration++)
        {
            byte[] data = new byte[random.Next(300)];
            random.NextBytes(data);
            pair.A.HandlePacket(data);
        }
        Check.Equal(SessionState.Running, pair.A.State, "Malformed traffic is ignored without parser exceptions");
        pair.Clock.NowMilliseconds += 501;
        pair.A.Poll(0, pair.Statuses);
        Check.Equal(SessionState.Disconnected, pair.A.State, "Rejected packets never reset the liveness timer");
    }

    private static ProtocolPacket Inputs(int acknowledged, int count, byte[] bytes) =>
        new()
        {
            Kind = PacketKind.Inputs,
            AcknowledgedFrame = acknowledged,
            Statuses = [new(false, -1), new(false, -1)],
            InputCount = count,
            InputData = bytes,
        };

    private static void ExclusionIsDifferentFromVoluntaryDeparture()
    {
        var pair = new Pair();
        pair.Synchronize();
        DrainEvents(pair.B);
        pair.A.Disconnect();
        pair.Deliver();
        Check.True(
            !DrainEvents(pair.B).Any(e => e.Kind == SessionEventKind.Excluded),
            "Voluntary departure does not exclude the remaining peer"
        );
        pair.A.Disconnect(excludeRemote: true);
        pair.Deliver();
        Check.True(
            DrainEvents(pair.B).Any(e => e.Kind == SessionEventKind.Excluded),
            "A disconnected endpoint still accepts an upgraded exclusion notice"
        );
        pair.Clock.NowMilliseconds += 101;
        pair.A.Poll(0, pair.Statuses);
        pair.Deliver();
        Check.True(
            !DrainEvents(pair.B).Any(e => e.Kind == SessionEventKind.Excluded),
            "Retransmitted exclusion notices do not duplicate exclusion events"
        );

        pair = new Pair();
        pair.Synchronize();
        pair.A.Disconnect(excludeRemote: true);
        pair.ClearPackets();
        pair.Clock.NowMilliseconds += 101;
        pair.A.Poll(0, pair.Statuses);
        pair.Deliver();
        Check.True(
            DrainEvents(pair.B).Any(e => e.Kind == SessionEventKind.Excluded),
            "Dropped exclusion notices are retried with their original meaning"
        );
    }

    private static void StructuredPacketAndCompressionFuzzStayWithinBounds()
    {
        var random = new Random(4401);
        byte[] header = PacketCodec.Encode(1, 9, new ProtocolPacket { Kind = PacketKind.Disconnect })[
            ..PacketCodec.HeaderSize
        ];
        for (int iteration = 0; iteration < 5000; iteration++)
        {
            byte[] packet = new byte[PacketCodec.HeaderSize + random.Next(300)];
            random.NextBytes(packet);
            header.CopyTo(packet, 0);
            packet[PacketCodec.HeaderSize - 1] = (byte)(iteration % 8);
            PacketCodec.TryDecode(packet, 1, 9, 2, out _, out _);

            byte[] compressed = new byte[random.Next(40)];
            random.NextBytes(compressed);
            int count = random.Next(1, 12);
            int size = random.Next(1, 8);
            if (InputCompression.TryDecode(compressed, count, size, out byte[][] frames))
            {
                Check.Equal(count, frames.Length, "Fuzzed compression never escapes the declared frame count");
                Check.True(
                    frames.All(frame => frame.Length == size),
                    "Fuzzed compression never escapes the declared input size"
                );
            }
        }
        Check.True(true, "Structured fuzzing completed without exceptions");

        foreach (PacketKind kind in Enum.GetValues<PacketKind>())
        {
            ProtocolPacket packet = Inputs(-1, 1, [0, 4]);
            packet.Kind = kind;
            byte[] encoded = PacketCodec.Encode(1, 9, packet);
            Check.True(
                PacketCodec.TryDecode(encoded, 1, 9, 2, out _, out _),
                "Every complete message type has a valid structural encoding"
            );
            for (int length = 0; length < encoded.Length; length++)
                Check.True(
                    !PacketCodec.TryDecode(encoded.AsSpan(0, length), 1, 9, 2, out _, out _),
                    "Every truncated message boundary is rejected"
                );
            Check.True(
                !PacketCodec.TryDecode([.. encoded, 0], 1, 9, 2, out _, out _),
                "Message decoders reject trailing bytes"
            );
        }
    }

    private static void DisconnectProgressSurvivesPacketReordering()
    {
        var pair = new Pair();
        pair.Synchronize();
        pair.ClearPackets();
        pair.A.SetSessionProgress(10, 2, 10);
        pair.Clock.NowMilliseconds += 40;
        pair.A.Poll(11, pair.Statuses);
        pair.A.SetSessionProgress(10, 2, 10, 12);
        pair.Clock.NowMilliseconds += 40;
        pair.A.Poll(13, pair.Statuses);
        pair.A.SetSessionProgress(12, 2, 10, 12);
        pair.Clock.NowMilliseconds += 40;
        pair.A.Poll(13, pair.Statuses);
        pair.A.SetSessionProgress(12, 3, 12);
        pair.Clock.NowMilliseconds += 40;
        pair.A.Poll(13, pair.Statuses);
        pair.A.SetSessionProgress(12, 3, 12, 12);
        pair.Clock.NowMilliseconds += 40;
        pair.A.Poll(13, pair.Statuses);
        byte[][] packets = pair.FromA.Packets.Select(packet => packet.Data).ToArray();
        foreach (byte[] packet in packets.Reverse())
            pair.B.HandlePacket(packet);
        Check.Equal(12, pair.B.RemoteCommittedFrame, "Reordered reports cannot retract committed history");
        Check.Equal(3u, pair.B.RemoteDisconnectMask, "Reordered reports cannot retract cumulative removals");
        Check.Equal(12, pair.B.RemoteDisconnectFloor, "The disconnect floor belongs to its corresponding mask");
        Check.Equal(
            12,
            pair.B.RemoteDisconnectReadyCut,
            "Reordered preparation packets cannot retract a ready certificate"
        );
        Check.Equal(SessionState.Running, pair.B.State, "Legitimate report reordering preserves the connection");
        Check.Throws<ArgumentException>(
            () => pair.A.SetSessionProgress(11, 3, 12, 12),
            "Committed progress cannot move backwards locally"
        );
        Check.Throws<ArgumentException>(
            () => pair.A.SetSessionProgress(12, 2, 10, 12),
            "Cumulative removals cannot shrink locally"
        );
        Check.Throws<ArgumentException>(
            () => pair.A.SetSessionProgress(12, 3, 11, 12),
            "A proposal's frozen floor cannot change"
        );

        ProtocolPacket contradictory = Inputs(-1, 0, []);
        contradictory.Frame = 15;
        contradictory.CommittedFrame = 14;
        contradictory.DisconnectMask = 3;
        contradictory.DisconnectFloor = 12;
        contradictory.DisconnectReadyCut = 13;
        pair.B.HandlePacket(PacketCodec.Encode(1, 9, contradictory));
        Check.Equal(SessionState.Disconnected, pair.B.State, "A peer cannot issue contradictory ready certificates");
        Check.Equal(
            12,
            pair.B.RemoteCommittedFrame,
            "Rejected agreement reports cannot partially change committed progress"
        );
    }

    private static List<SessionEvent> DrainEvents(PeerProtocol protocol)
    {
        var result = new List<SessionEvent>();
        while (protocol.TryGetEvent(out SessionEvent sessionEvent))
            result.Add(sessionEvent);
        return result;
    }

    private static SessionOptions SmallOptions() =>
        new()
        {
            HistoryFrames = 96,
            MaxPredictionFrames = 12,
            MaxInputDelay = 4,
            SpectatorBufferFrames = 0,
        };

    private sealed class TestClock : IClock
    {
        public long NowMilliseconds { get; set; }
    }

    private sealed class CaptureTransport(TestClock clock, int delayMilliseconds) : ITransport
    {
        public List<(long Due, byte[] Data)> Packets { get; } = [];

        public void Send(int peerId, ReadOnlySpan<byte> packet) =>
            Packets.Add((clock.NowMilliseconds + delayMilliseconds, packet.ToArray()));

        public bool TryReceive(out Datagram datagram)
        {
            datagram = default;
            return false;
        }
    }

    private sealed class Pair
    {
        public readonly TestClock Clock = new();
        public readonly CaptureTransport FromA;
        public readonly CaptureTransport FromB;
        public readonly PeerProtocol A;
        public readonly PeerProtocol B;
        public readonly SessionOptions Options;
        public readonly ConnectionStatus[] Statuses;
        private readonly int initialFrame;

        public Pair(
            int inputSize = 1,
            int? reverseSize = null,
            SessionOptions? options = null,
            int delayMilliseconds = 0,
            int initialFrame = 0
        )
        {
            Options = options ?? SmallOptions();
            this.initialFrame = initialFrame;
            Statuses = [new(false, initialFrame - 1), new(false, initialFrame - 1)];
            FromA = new CaptureTransport(Clock, delayMilliseconds);
            FromB = new CaptureTransport(Clock, delayMilliseconds);
            A = new PeerProtocol(1, 1, 9, inputSize, reverseSize ?? inputSize, 2, Options, Clock, FromA, initialFrame);
            B = new PeerProtocol(0, 1, 9, reverseSize ?? inputSize, inputSize, 2, Options, Clock, FromB, initialFrame);
        }

        public void Synchronize()
        {
            for (
                int attempt = 0;
                attempt < 450 && (A.State != SessionState.Running || B.State != SessionState.Running);
                attempt++
            )
                Step(10);
            Check.Equal(SessionState.Running, A.State, "Test peer A synchronized");
            Check.Equal(SessionState.Running, B.State, "Test peer B synchronized");
            Deliver();
        }

        public void Step(int milliseconds)
        {
            Clock.NowMilliseconds += milliseconds;
            Deliver();
            A.Poll(initialFrame, Statuses);
            B.Poll(initialFrame, Statuses);
            Deliver();
        }

        public void Deliver()
        {
            for (int pass = 0; pass < 100; pass++)
            {
                bool any = Deliver(FromA, B) | Deliver(FromB, A);
                if (!any)
                    return;
            }
            throw new InvalidOperationException("Protocol test generated an unbounded packet exchange.");
        }

        private bool Deliver(CaptureTransport from, PeerProtocol to)
        {
            var ready = from.Packets.Where(packet => packet.Due <= Clock.NowMilliseconds).ToArray();
            foreach (var packet in ready)
            {
                from.Packets.Remove(packet);
                to.HandlePacket(packet.Data);
            }
            return ready.Length > 0;
        }

        public void ClearPackets()
        {
            FromA.Packets.Clear();
            FromB.Packets.Clear();
        }
    }
}
