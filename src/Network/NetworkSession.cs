using System.Security.Cryptography;
using FrogSmashers.Core;
using GGCS;

namespace FrogSmashers.Network;

public sealed class NetworkSession : IRollbackSession
{
    private readonly SessionConfig config;
    private readonly IPeerTransport transport;
    private readonly IRollbackSimulation simulation;
    private readonly P2PSession<RollbackInput, byte[]>? playing;
    private readonly SpectatorSession<RollbackInput, byte[]>? spectator;
    private readonly SpectatorPlayback? spectatorPlayback;
    private readonly SortedDictionary<long, SimulationEvent[]> eventJournal = new();
    private readonly long initialTick;
    private long? stopAtTick;
    private int unknownPackets;
    private bool disposed;
    private RollbackPreferences rollback;
    private AdvanceStatus lastAdvanceStatus = AdvanceStatus.Synchronizing;

    public World World => simulation.World;
    public byte[] PreviousSnapshot { get; private set; }
    public string? Error { get; private set; }
    public bool IsTransportFailure { get; private set; }
    public int LocalPeer => config.LocalPeer;
    public int[] LocalSlots => config.PeerSlots[LocalPeer];

    public int PlayerSlot(int handle) => config.InputPlayerSlots[handle];

    public int InputHandle(int slot) => Array.IndexOf(config.InputPlayerSlots, slot);

    public int HostCommandHandle => Array.IndexOf(config.InputPlayerSlots, -1);
    public long ConfirmedFrame => initialTick + (playing?.ConfirmedFrame ?? spectator!.CurrentFrame - 1);
    public int PredictionDepth => playing?.PredictionDepth ?? 0;
    public int MaxPrediction => config.MaxPrediction;
    private bool CanShowInputWait =>
        Error == null && State == SessionState.Running && (!stopAtTick.HasValue || World.TickNumber < stopAtTick.Value);
    public IReadOnlyList<int> WaitingForInputPlayers
    {
        get
        {
            if (!CanShowInputWait)
                return [];
            return lastAdvanceStatus == AdvanceStatus.PredictionLimit
                ? Enumerable
                    .Range(0, config.InputCount)
                    .Where(handle => PredictionForPlayer(handle) >= config.MaxPrediction)
                    .ToArray()
                : [];
        }
    }
    public bool WaitingForHostInputs =>
        CanShowInputWait
        && spectator != null
        && lastAdvanceStatus == AdvanceStatus.WaitingForInput
        && spectator.BufferedFrames == 0;

    public int PlayerPeer(int handle) => Array.FindIndex(config.PeerSlots, slots => slots.Contains(handle));

    public int PredictionForPlayer(int handle)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(handle);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(handle, config.InputCount);
        return playing?.PredictionForPlayer(handle) ?? 0;
    }

    public long RollbackCount { get; private set; }
    public long ResimulatedTicks { get; private set; }
    public long LastRollbackFromFrame { get; private set; } = -1;
    public int FramesAheadOfPeers => playing?.FramesAhead ?? 0;
    public double FrameDurationMultiplier =>
        playing != null
            ? playing.RecommendedFrameDurationMultiplier
            : spectatorPlayback!.FrameDurationMultiplier(spectator!.BufferedFrames);
    public int BufferedFrames => playing?.ConfirmedInputFramesAvailable ?? spectator!.BufferedFrames;
    public IReadOnlyList<int> AcceptedLocalSlots { get; private set; } = [];
    public bool LocalInputSubmitted => AcceptedLocalSlots.Count > 0;
    public long LastSubmittedTick => initialTick + (playing?.LastSubmittedFrame ?? -1);
    public long StartTick => initialTick;
    public string WaitReason { get; private set; } = "SYNCHRONIZING";
    public SessionState State => playing?.State ?? spectator!.State;
    public IReadOnlyList<PeerNetworkStats> PeerStats => playing?.NetworkStats ?? [spectator!.NetworkStats];
    public int RejectedPackets => unknownPackets + (int)PeerStats.Sum(stats => stats.InvalidPackets);

    public NetworkSession(World world, SessionConfig config, IPeerTransport transport)
        : this(new MatchSimulation(world, config.InputPlayerSlots), config, transport) { }

    public NetworkSession(IRollbackSimulation simulation, SessionConfig config, IPeerTransport transport)
    {
        this.simulation = simulation;
        this.config = config;
        rollback = config.Rollback;
        this.transport = transport;
        initialTick = World.TickNumber;
        PreviousSnapshot = World.Capture();
        var options = new SessionOptions
        {
            FramesPerSecond = World.TickRate,
            InputDelay = config.Rollback.Delay,
            MaxInputDelay = RollbackPreferences.InputCapacityFrames,
            MaxPredictionFrames = config.MaxPrediction,
            HistoryFrames = config.HistoryFrames,
            ChecksumInterval = World.TickRate / 2,
            MaxPacketBytes = 1185,
            SynchronizationRoundTrips = 1,
        };
        var players = config
            .PeerSlots.SelectMany((slots, peer) => slots.Select(slot => new Player(slot, peer)))
            .OrderBy(player => player.Handle)
            .ToArray();
        var adapter = new TransportAdapter(this);
        var game = new GameAdapter(this);
        var codec = new RollbackInputCodec();
        string schema =
            Convert.ToHexString(config.Fingerprint) + Convert.ToHexString(SHA256.HashData(simulation.Capture()));
        if (LocalSlots.Length > 0)
        {
            playing = new(
                config.Generation,
                LocalPeer,
                players,
                game,
                codec,
                adapter,
                options,
                adapter,
                (input, _) => new(input.Gameplay),
                schema
            );
            if (LocalPeer == 0)
                foreach (int peer in config.ActivePeers)
                    if (peer != 0 && config.PeerSlots[peer].Length == 0)
                        playing.AddSpectator(peer);
            playing.SetTiming(config.Rollback.Timing);
            int pendingDelay = LocalSlots
                .Select(handle => config.InitialInputs.GetValueOrDefault(handle)?.Length ?? 0)
                .DefaultIfEmpty()
                .Max();
            playing.RestoreExtraDelay(Math.Clamp(pendingDelay - rollback.Delay, 0, rollback.MaxExtraDelay));
            foreach (int handle in LocalSlots)
                if (config.InitialInputs.TryGetValue(handle, out var inputs))
                    playing.SeedLocalInputDelay(handle, inputs);
        }
        else
        {
            spectator = new(config.Generation, 0, players, game, codec, adapter, options, adapter, inputSchema: schema);
            spectatorPlayback = new();
        }
    }

    public IEnumerable<SimulationEvent> EventsSince(long frame) =>
        eventJournal.Where(pair => pair.Key >= frame).SelectMany(pair => pair.Value);

    public RollbackPreferences RollbackSettings => rollback;
    public int ExtraDelayFrames => playing?.ExtraDelayFrames ?? 0;
    public int EffectiveDelayFrames => playing?.EffectiveDelayFrames ?? 0;

    public void SetTiming(RollbackPreferences preferences)
    {
        if (!preferences.IsValid)
            throw new ArgumentException("Invalid rollback preferences", nameof(preferences));
        rollback = preferences;
        playing?.SetTiming(preferences.Timing);
    }

    public void StopAtTick(long? stateTick)
    {
        if (stateTick < World.TickNumber)
            throw new ArgumentOutOfRangeException(nameof(stateTick), "A pause boundary cannot be in the past");
        stopAtTick = stateTick;
        playing?.FreezeTiming(stateTick.HasValue);
    }

    public bool TryGetConfirmedCheckpoint(long stateTick, out byte[] snapshot)
    {
        snapshot = [];
        int frame = SessionFrame(stateTick);
        if (frame == 0 && World.TickNumber == initialTick)
        {
            snapshot = simulation.Capture();
            return true;
        }
        if (playing != null && playing.TryGetConfirmedState(frame, out var state))
        {
            snapshot = state.State.ToArray();
            return true;
        }
        if (spectator != null && stateTick == World.TickNumber)
        {
            snapshot = simulation.Capture();
            return true;
        }
        return false;
    }

    public RollbackInput[][] ExportPendingLocalInputs(long boundaryStateTick)
    {
        if (
            World.TickNumber != boundaryStateTick
            || ConfirmedFrame < boundaryStateTick - 1
            || LocalSlots.Length > 0 && LastSubmittedTick >= boundaryStateTick
        )
            throw new InvalidOperationException("Export pending inputs at a confirmed pause boundary");
        var result = new RollbackInput[LocalSlots.Length][];
        for (int player = 0; player < LocalSlots.Length; player++)
        {
            int count = playing!.PendingInputCount(LocalSlots[player], SessionFrame(boundaryStateTick));
            if (boundaryStateTick == initialTick && count == 0)
                count = config.InitialInputs.TryGetValue(LocalSlots[player], out var initial)
                    ? initial.Length
                    : EffectiveDelayFrames;
            result[player] = new RollbackInput[count];
            for (int offset = 0; offset < count; offset++)
                if (
                    !playing!.TryGetSubmittedInput(
                        LocalSlots[player],
                        SessionFrame(boundaryStateTick) + offset,
                        out result[player][offset]
                    )
                )
                {
                    if (boundaryStateTick != initialTick)
                        throw new InvalidOperationException(
                            "Pending local input is missing from the checkpoint boundary"
                        );
                    result[player][offset] = config.InitialInputs.TryGetValue(LocalSlots[player], out var seeded)
                        ? seeded[offset]
                        : default;
                }
        }
        return result;
    }

    public bool AllPeersConfirmed(long inputTick)
    {
        if (Error != null || ConfirmedFrame < inputTick)
            return false;
        if (inputTick == initialTick - 1)
            return true;
        return playing?.ConfirmState(SessionFrame(inputTick + 1)) ?? true;
    }

    public void DisconnectPeer(int peer)
    {
        if (!PeerStats.Any(stats => stats.PeerId == peer))
            return;
        if (playing != null)
            playing.DisconnectPeer(peer);
        else if (peer == 0)
        {
            Error = "Host disconnected";
            IsTransportFailure = true;
        }
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
        playing?.Poll();
        spectator?.Poll();
        DrainEvents();
        if (stopAtTick.HasValue && World.TickNumber >= stopAtTick.Value)
        {
            WaitReason = "PAUSED FOR LOBBY CHANGE";
            playing?.ConfirmState(SessionFrame(stopAtTick.Value));
        }
        if (World.Match.Phase == MatchPhase.MatchFinished)
            playing?.ConfirmState(SessionFrame(World.TickNumber));
    }

    public bool TryAdvance(InputFrame[] localInputs) =>
        TryAdvance(localInputs.Select(input => new RollbackInput(input)).ToArray());

    public bool TryAdvance(RollbackInput[] localInputs)
    {
        if (localInputs.Length != LocalSlots.Length)
            throw new ArgumentException("Pass inputs in LocalSlots order", nameof(localInputs));
        AcceptedLocalSlots = [];
        if (Error != null || stopAtTick.HasValue && World.TickNumber >= stopAtTick.Value)
            return false;
        int previousSubmission = playing?.LastSubmittedFrame ?? -1;
        AdvanceStatus result = playing?.AdvanceFrame(localInputs) ?? AdvanceSpectator();
        lastAdvanceStatus = result;
        if (playing != null && playing.LastSubmittedFrame != previousSubmission)
            AcceptedLocalSlots = LocalSlots
                .Where(handle => playing.LastAcceptedInputFrame(handle) == playing.LastSubmittedFrame)
                .ToArray();
        WaitReason = result switch
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
        return result == AdvanceStatus.Advanced && Error == null;
    }

    private AdvanceStatus AdvanceSpectator()
    {
        spectator!.Poll();
        if (
            spectator.State == SessionState.Running
            && !spectatorPlayback!.Ready(
                spectator.CurrentFrame,
                spectator.BufferedFrames,
                transport.TimeMilliseconds,
                stopAtTick.HasValue
            )
        )
            return AdvanceStatus.WaitingForInput;
        return spectator.AdvanceFrame(drain: true);
    }

    private int SessionFrame(long tick) => checked((int)(tick - initialTick));

    private void DrainEvents()
    {
        while (TryGetEvent(out var item))
        {
            bool activeLink = item.PeerId == 0 || config.PeerSlots[item.PeerId].Length > 0;
            switch (item.Kind)
            {
                case SessionEventKind.DesyncDetected:
                    Error = $"Desync with peer {item.PeerId} at state {initialTick + item.Frame}: {item.Detail}";
                    break;
                case SessionEventKind.ProtocolError when activeLink:
                    Error = item.Detail ?? "Invalid rollback protocol";
                    break;
                case SessionEventKind.Excluded:
                    Error = item.Detail ?? "Connection lost";
                    IsTransportFailure = true;
                    break;
                case SessionEventKind.Disconnected when spectator != null:
                    Error = "Host disconnected";
                    IsTransportFailure = true;
                    break;
                case SessionEventKind.Disconnected when activeLink:
                    WaitReason = "AGREEING DISCONNECT";
                    break;
                case SessionEventKind.SpectatorTooFarBehind when spectator != null:
                    Error = "Spectator needs a new checkpoint";
                    IsTransportFailure = true;
                    break;
            }
        }
    }

    private bool TryGetEvent(out SessionEvent item) =>
        playing != null ? playing.TryGetEvent(out item) : spectator!.TryGetEvent(out item);

    private sealed class GameAdapter(NetworkSession owner) : IRollbackGame<RollbackInput, byte[]>
    {
        private readonly RollbackInput[] commands = new RollbackInput[owner.config.InputCount];
        private byte[]? capturedWorld;
        private long capturedTick = -1;

        public SavedState<byte[]> SaveState()
        {
            byte[] snapshot = owner.simulation.Capture(out capturedWorld);
            capturedTick = owner.World.TickNumber;
            return new(snapshot, World.Hash(snapshot));
        }

        public void LoadState(byte[] state)
        {
            owner.simulation.Restore(state, out capturedWorld);
            capturedTick = owner.World.TickNumber;
            owner.RollbackCount++;
            owner.LastRollbackFromFrame = owner.World.TickNumber;
        }

        public void AdvanceFrame(int frame, ReadOnlySpan<PlayerInput<RollbackInput>> inputs, bool isResimulation)
        {
            if (owner.World.TickNumber != owner.initialTick + frame)
                throw new InvalidOperationException("Rollback frame and world tick disagree");
            if (inputs.Length != commands.Length)
                throw new ArgumentException("Supply one input per session handle", nameof(inputs));
            for (int index = 0; index < inputs.Length; index++)
                commands[index] = inputs[index].Status == InputStatus.Disconnected ? default : inputs[index].Input;
            owner.PreviousSnapshot = capturedTick == owner.World.TickNumber ? capturedWorld! : owner.World.Capture();
            long tick = owner.World.TickNumber;
            owner.simulation.Tick(commands);
            owner.eventJournal[tick] = owner.simulation.Events.ToArray();
            if (isResimulation)
                owner.ResimulatedTicks++;
            long floor = owner.World.TickNumber - owner.config.HistoryFrames;
            foreach (long old in owner.eventJournal.Keys.TakeWhile(key => key < floor).ToArray())
                owner.eventJournal.Remove(old);
        }
    }

    private sealed class TransportAdapter(NetworkSession owner) : ITransport, IClock
    {
        public long NowMilliseconds => owner.transport.TimeMilliseconds;

        public void Send(int peerId, ReadOnlySpan<byte> packet) => owner.transport.Send(peerId, packet);

        public bool TryReceive(out GGCS.Datagram datagram)
        {
            while (owner.transport.TryReceive(out var incoming))
            {
                if (!owner.config.ActivePeers.Contains(incoming.Peer) || incoming.Peer == owner.LocalPeer)
                {
                    owner.unknownPackets++;
                    continue;
                }
                datagram = new(incoming.Peer, incoming.Data);
                return true;
            }
            datagram = default;
            return false;
        }
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
