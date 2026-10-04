using FrogSmashers.Core;
using GGCS;

namespace FrogSmashers.Network;

public sealed class LobbyNetworkSession : IRollbackSession
{
    private const int SpectatorPeerOffset = 16;
    private readonly LobbySimulation simulation;
    private readonly IPeerTransport transport;
    private readonly SessionOptions options;
    private readonly Player[] players = Enumerable
        .Range(0, LobbyRoster.MaxPeers)
        .Select(peer => new Player(peer, peer))
        .ToArray();
    private readonly Queue<GGCS.Datagram> meshPackets = new();
    private readonly Queue<GGCS.Datagram> spectatorPackets = new();
    private readonly SortedDictionary<long, SimulationEvent[]> eventJournal = new();
    private readonly P2PSession<LobbyFrame, byte[]>? playing;
    private readonly SpectatorSession<LobbyFrame, byte[]>? spectator;
    private readonly SpectatorPlayback playback = new();
    private long? stopAtTick;
    private AdvanceStatus lastAdvance;
    private bool disposed;
    internal LobbyRosterCommand ProposedMembership { get; set; }

    public LobbyNetworkSession(
        LobbySimulation simulation,
        int localPeer,
        ulong sessionId,
        string schema,
        IPeerTransport transport,
        RollbackPreferences preferences,
        bool spectating,
        IReadOnlyList<int> connectedPeers,
        SessionDisconnectState? disconnectState = null
    )
    {
        this.simulation = simulation;
        this.transport = transport;
        LocalPeer = localPeer;
        RollbackSettings = preferences;
        StartTick = simulation.World.TickNumber;
        PreviousSnapshot = World.Capture();
        ProposedMembership = LobbyRosterCommand.From(simulation.RosterRevision, simulation.Membership);
        options = new SessionOptions
        {
            FramesPerSecond = World.TickRate,
            InputDelay = preferences.Delay,
            MaxInputDelay = RollbackPreferences.InputCapacityFrames,
            MaxPredictionFrames = RollbackPreferences.PredictionFrames,
            HistoryFrames = RollbackPreferences.HistoryFrames,
            ChecksumInterval = World.TickRate / 2,
            MaxPacketBytes = 1184,
            SynchronizationRoundTrips = 1,
        };
        var game = new GameAdapter(this);
        var codec = new LobbyFrameCodec();
        var adapter = new TransportAdapter(this, spectating);
        int first = checked((int)StartTick);
        if (spectating)
            spectator = new(
                SessionLink.Identity(sessionId, 0, localPeer + SpectatorPeerOffset, first),
                0,
                players,
                game,
                codec,
                adapter,
                options,
                adapter,
                first,
                schema,
                codec
            );
        else
        {
            var initial = new LobbyFrame { Membership = ProposedMembership };
            var membership = new SessionParticipation<LobbyFrame>(0, initial, input => input.Membership.Players);
            playing = new(
                sessionId,
                localPeer,
                players,
                game,
                codec,
                adapter,
                options,
                adapter,
                LobbyFrame.Predict,
                schema,
                membership,
                first,
                connectedPeers,
                codec,
                disconnectState
            );
            playing.SetTiming(preferences.Timing);
        }
    }

    public World World => simulation.World;
    public byte[] PreviousSnapshot { get; private set; }
    public string? Error { get; private set; }
    public bool IsTransportFailure { get; private set; }
    public int LocalPeer { get; }
    public int[] LocalSlots =>
        simulation
            .InputSources.Select((source, handle) => (source, handle))
            .Where(item => item.source.Peer == LocalPeer)
            .Select(item => item.handle)
            .ToArray();

    public int PlayerSlot(int handle) => simulation.InputRooms[handle];

    public int InputHandle(int slot) => simulation.InputRooms.ToList().IndexOf(slot);

    public int HostCommandHandle => 0;
    public long ConfirmedFrame => playing?.ConfirmedFrame ?? spectator!.CurrentFrame - 1;
    public int PredictionDepth => playing?.PredictionDepth ?? 0;
    public int MaxPrediction => options.MaxPredictionFrames;
    public IReadOnlyList<int> WaitingForInputPlayers =>
        lastAdvance == AdvanceStatus.PredictionLimit
            ? Enumerable
                .Range(0, simulation.InputSources.Count)
                .Where(handle => PredictionForPlayer(handle) >= MaxPrediction)
                .ToArray()
            : [];
    public bool WaitingForHostInputs =>
        spectator != null && lastAdvance == AdvanceStatus.WaitingForInput && spectator.BufferedFrames == 0;

    public int PlayerPeer(int handle) => simulation.InputSources[handle].Peer;

    public int PredictionForPlayer(int handle) => playing?.PredictionForPlayer(PlayerPeer(handle)) ?? 0;

    public long RollbackCount { get; private set; }
    public long ResimulatedTicks { get; private set; }
    public long LastRollbackFromFrame { get; private set; } = -1;
    public int FramesAheadOfPeers => playing?.FramesAhead ?? 0;
    public double FrameDurationMultiplier =>
        playing?.RecommendedFrameDurationMultiplier ?? playback.FrameDurationMultiplier(BufferedFrames);
    public int BufferedFrames => playing?.ConfirmedInputFramesAvailable ?? spectator!.BufferedFrames;
    public IReadOnlyList<int> AcceptedLocalSlots { get; private set; } = [];
    public bool LocalInputSubmitted { get; private set; }
    public long LastSubmittedTick => playing?.LastSubmittedFrame ?? -1;
    public long StartTick { get; }
    public string WaitReason { get; private set; } = "";
    public SessionState State => playing?.State ?? spectator!.State;
    public IReadOnlyList<PeerNetworkStats> PeerStats =>
        playing?.NetworkStats.Where(stats => stats.PeerId < SpectatorPeerOffset).ToArray() ?? [spectator!.NetworkStats];
    public int RejectedPackets => (int)PeerStats.Sum(stats => stats.InvalidPackets);
    public RollbackPreferences RollbackSettings { get; private set; }
    public int ExtraDelayFrames => playing?.ExtraDelayFrames ?? 0;
    public int EffectiveDelayFrames => playing?.EffectiveDelayFrames ?? 0;
    internal bool Spectating => spectator != null;
    internal bool CanPreparePeer => playing?.CanPreparePeer == true;

    internal SessionDisconnectState CaptureDisconnects() => playing!.CaptureDisconnects();

    public IEnumerable<SimulationEvent> EventsSince(long frame) =>
        eventJournal.Where(pair => pair.Key >= frame).SelectMany(pair => pair.Value);

    public void SetTiming(RollbackPreferences preferences)
    {
        if (!preferences.IsValid)
            throw new ArgumentException("Invalid rollback preferences");
        RollbackSettings = preferences;
        playing?.SetTiming(preferences.Timing);
    }

    public void StopAtTick(long? stateTick)
    {
        if (stateTick < World.TickNumber)
            throw new ArgumentOutOfRangeException(nameof(stateTick));
        stopAtTick = stateTick;
        playing?.FreezeTiming(stateTick.HasValue);
    }

    public bool TryGetConfirmedCheckpoint(long tick, out byte[] snapshot)
    {
        snapshot = [];
        if (playing != null && playing.TryGetConfirmedState(checked((int)tick), out var state))
        {
            snapshot = state.State;
            return true;
        }
        if (tick == World.TickNumber && (spectator != null || tick == StartTick))
        {
            snapshot = simulation.Capture();
            return true;
        }
        return false;
    }

    public bool AllPeersConfirmed(long inputTick) =>
        ConfirmedFrame >= inputTick && (playing?.ConfirmState(checked((int)inputTick + 1)) ?? true);

    public void DisconnectPeer(int peer)
    {
        if (playing != null && PeerStats.Any(stats => stats.PeerId == peer))
            playing.DisconnectPeer(peer);
        else if (peer == 0)
        {
            Error = "HOST DISCONNECTED";
            IsTransportFailure = true;
        }
    }

    internal bool ConnectPeer(int peer, long firstFrame)
    {
        if (playing == null)
            throw new InvalidOperationException("Spectator must prepare a playing session first");
        if (firstFrame > World.TickNumber || firstFrame < Math.Max(StartTick, World.TickNumber - options.HistoryFrames))
            return false;
        return playing.ConnectPeer(peer, checked((int)firstFrame));
    }

    internal void CatchUp()
    {
        if (playing == null || simulation.Membership.Humans(LocalPeer).Length > 0)
            return;
        int target = AdmissionTarget;
        for (int count = 0; count < 30 && World.TickNumber < target; count++)
            if (!TryAdvance(new RollbackInput[LocalSlots.Length]))
                break;
    }

    internal bool CaughtUp => World.TickNumber >= AdmissionTarget;

    private int AdmissionTarget
    {
        get
        {
            var host = PeerStats.FirstOrDefault(stats => stats.PeerId == 0);
            int lead =
                (int)Math.Ceiling(host.RoundTripMilliseconds * World.TickRate / 2000)
                - host.DonationFrames
                - EffectiveDelayFrames
                + RollbackSettings.Donation;
            return host.LastReceivedFrame + Math.Min(MaxPrediction - 2, lead);
        }
    }

    internal void AddSpectator(int peer, long firstFrame)
    {
        playing!.AddSpectator(peer + SpectatorPeerOffset, checked((int)firstFrame));
    }

    internal void RemoveSpectator(int peer)
    {
        if (playing?.NetworkStats.Any(stats => stats.PeerId == peer + SpectatorPeerOffset) == true)
            playing.RemoveSpectator(peer + SpectatorPeerOffset);
    }

    public void Poll()
    {
        if (disposed || Error != null)
            return;
        if (transport.Error != null)
        {
            Error = transport.Error;
            IsTransportFailure = true;
            return;
        }
        while (transport.TryReceive(out var packet))
        {
            if (packet.Data.Length < 2)
                continue;
            bool observer = packet.Data[0] == 1;
            int peer = observer && LocalPeer == 0 ? packet.Peer + SpectatorPeerOffset : packet.Peer;
            var datagram = new GGCS.Datagram(peer, packet.Data[1..]);
            if (observer && LocalPeer != 0)
                spectatorPackets.Enqueue(datagram);
            else
                meshPackets.Enqueue(datagram);
        }
        playing?.Poll();
        spectator?.Poll();
        DrainEvents();
    }

    public bool TryAdvance(RollbackInput[] inputs)
    {
        int[] handles = LocalSlots;
        if (inputs.Length != handles.Length)
            throw new ArgumentException("Pass local inputs in handle order");
        AcceptedLocalSlots = [];
        LocalInputSubmitted = false;
        if (Error != null || stopAtTick.HasValue && World.TickNumber >= stopAtTick.Value)
            return false;
        if (playing != null)
        {
            var input = new LobbyFrame();
            int submittedDevices = 0;
            if (LocalPeer == 0)
                input.Membership = ProposedMembership;
            for (int index = 0; index < inputs.Length; index++)
            {
                var source = simulation.InputSources[handles[index]];
                if (source.HostCommand)
                    input.Cpu = inputs[index].Cpu;
                else
                {
                    input.Controls[source.Id] = inputs[index];
                    submittedDevices |= 1 << source.Id;
                }
            }
            int previous = playing.LastSubmittedFrame;
            lastAdvance = playing.AdvanceFrame([input]);
            if (
                playing.LastSubmittedFrame != previous
                && playing.LastAcceptedInputFrame(LocalPeer) == playing.LastSubmittedFrame
            )
            {
                LocalInputSubmitted = true;
                AcceptedLocalSlots = LocalSlots
                    .Where(handle =>
                        simulation.InputSources[handle].HostCommand
                        || (submittedDevices & (1 << simulation.InputSources[handle].Id)) != 0
                    )
                    .ToArray();
            }
        }
        else
        {
            spectator!.Poll();
            lastAdvance =
                spectator.State == SessionState.Running
                && !playback.Ready(
                    spectator.CurrentFrame,
                    spectator.BufferedFrames,
                    transport.TimeMilliseconds,
                    stopAtTick.HasValue
                )
                    ? AdvanceStatus.WaitingForInput
                    : spectator.AdvanceFrame(drain: true);
        }
        WaitReason = lastAdvance switch
        {
            AdvanceStatus.Advanced => "",
            AdvanceStatus.Synchronizing => "SYNCHRONIZING",
            AdvanceStatus.PredictionLimit => "WAITING FOR INPUT",
            AdvanceStatus.WaitingForInput => "WAITING FOR HOST",
            AdvanceStatus.InputBufferFull => "WAITING FOR ACKNOWLEDGEMENT",
            AdvanceStatus.DisconnectAgreement => "AGREEING DISCONNECT",
            _ => "DISCONNECTED",
        };
        DrainEvents();
        return lastAdvance == AdvanceStatus.Advanced && Error == null;
    }

    private void DrainEvents()
    {
        while (playing != null ? playing.TryGetEvent(out var item) : spectator!.TryGetEvent(out item))
        {
            if (item.Kind == SessionEventKind.DesyncDetected)
                Error = $"Lobby desync with peer {item.PeerId}: {item.Detail}";
            if (
                item.Kind == SessionEventKind.ProtocolError
                && (item.PeerId == 0 || simulation.Membership.Humans(item.PeerId).Length > 0)
            )
                Error = item.Detail;
            if (
                item.Kind == SessionEventKind.Excluded
                || spectator != null && item.Kind == SessionEventKind.Disconnected
            )
            {
                Error = "CONNECTION LOST";
                IsTransportFailure = true;
            }
        }
    }

    private sealed class GameAdapter(LobbyNetworkSession owner) : IRollbackGame<LobbyFrame, byte[]>
    {
        private byte[]? world;
        private long capturedTick = -1;

        public SavedState<byte[]> SaveState()
        {
            var state = owner.simulation.Capture(out world);
            capturedTick = owner.World.TickNumber;
            return new(state, World.Hash(state));
        }

        public void LoadState(byte[] state)
        {
            owner.simulation.Restore(state, out world);
            capturedTick = owner.World.TickNumber;
            owner.RollbackCount++;
            owner.LastRollbackFromFrame = capturedTick;
        }

        public void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<LobbyFrame>> inputs, bool isResimulation)
        {
            if (frame != owner.World.TickNumber)
                throw new InvalidOperationException("Lobby and rollback ticks disagree");
            owner.PreviousSnapshot = capturedTick == frame ? world! : owner.World.Capture();
            owner.simulation.TickFrame(inputs);
            owner.eventJournal[frame] = owner.simulation.Events.ToArray();
            if (isResimulation)
                owner.ResimulatedTicks++;
            foreach (
                long expired in owner
                    .eventJournal.Keys.TakeWhile(tick => tick < frame - owner.options.HistoryFrames)
                    .ToArray()
            )
                owner.eventJournal.Remove(expired);
        }
    }

    private sealed class TransportAdapter(LobbyNetworkSession owner, bool spectator) : ITransport, IClock
    {
        public long NowMilliseconds => owner.transport.TimeMilliseconds;

        public void Send(int peer, ReadOnlySpan<byte> packet)
        {
            bool observer = spectator || peer >= SpectatorPeerOffset;
            byte[] data = new byte[packet.Length + 1];
            data[0] = observer ? (byte)1 : (byte)0;
            packet.CopyTo(data.AsSpan(1));
            owner.transport.Send(peer >= SpectatorPeerOffset ? peer - SpectatorPeerOffset : peer, data);
        }

        public bool TryReceive(out GGCS.Datagram packet) =>
            (spectator ? owner.spectatorPackets : owner.meshPackets).TryDequeue(out packet);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        playing?.Close();
        spectator?.Close();
        transport.Dispose();
    }
}
