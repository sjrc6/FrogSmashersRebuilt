using GGCS.Protocol;

namespace GGCS;

public sealed class SpectatorSession<TInput, TState>
    where TInput : unmanaged
{
    private readonly int hostPeerId;
    private readonly ITransport transport;
    private readonly IRollbackGame<TInput, TState> game;
    private readonly SessionOptions options;
    private readonly SessionWire<TInput> wire;
    private readonly PeerProtocol host;
    private readonly Queue<(int Frame, PlayerInput<TInput>[] Inputs)> inputs = new();
    private readonly ConnectionStatus[] statuses;
    private readonly SessionEvents events = new();
    private bool stopped;

    public SpectatorSession(
        ulong sessionId,
        int hostPeerId,
        IReadOnlyList<Player> players,
        IRollbackGame<TInput, TState> game,
        IInputCodec<TInput> codec,
        ITransport transport,
        SessionOptions? options = null,
        IClock? clock = null,
        int startFrame = 0,
        string inputSchema = ""
    )
    {
        if (sessionId == 0 || hostPeerId < 0 || startFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(transport);
        this.hostPeerId = hostPeerId;
        this.game = game;
        this.transport = transport;
        this.options = options ?? new();
        wire = new(players, codec, this.options, inputSchema);
        CurrentFrame = startFrame;
        statuses = Enumerable.Repeat(new ConnectionStatus(false, -1), wire.Players.Length).ToArray();
        host = new(
            hostPeerId,
            sessionId,
            wire.ConfigurationId,
            0,
            wire.SpectatorInputSize,
            wire.Players.Length,
            this.options,
            clock ?? new MonotonicClock(),
            transport,
            startFrame
        );
    }

    public int CurrentFrame { get; private set; }
    public int BufferedFrames => inputs.Count;
    public SessionState State => stopped ? SessionState.Disconnected : host.State;
    public PeerNetworkStats NetworkStats => host.Stats;

    public bool TryGetEvent(out SessionEvent item) => events.TryGet(out item);

    public void Poll()
    {
        if (stopped)
            return;
        for (int i = 0; i < options.MaxPacketsPerPoll && transport.TryReceive(out var datagram); i++)
            if (datagram.PeerId == hostPeerId)
                host.HandlePacket(datagram.Data.Span);
        host.Poll(CurrentFrame, statuses);
        while (host.TryGetEvent(out var item))
            events.Add(item);
        while (host.TryReceiveInput(out var packet))
        {
            if (inputs.Count >= options.HistoryFrames)
            {
                events.Add(
                    new(
                        SessionEventKind.SpectatorTooFarBehind,
                        hostPeerId,
                        CurrentFrame,
                        "Spectator buffer is full; a new checkpoint is required."
                    )
                );
                Close();
                return;
            }
            try
            {
                inputs.Enqueue((packet.Frame, wire.DecodeSpectator(packet.Data)));
            }
            catch (Exception exception)
                when (exception is ArgumentException or FormatException or OverflowException or InvalidDataException)
            {
                events.Add(new(SessionEventKind.ProtocolError, hostPeerId, packet.Frame, exception.Message));
                Close();
                return;
            }
        }
    }

    public AdvanceStatus AdvanceFrame(bool drain = false)
    {
        Poll();
        if (State == SessionState.Disconnected)
            return AdvanceStatus.Disconnected;
        if (State == SessionState.Synchronizing)
            return AdvanceStatus.Synchronizing;
        if (inputs.Count <= (drain ? 0 : options.SpectatorBufferFrames))
            return AdvanceStatus.WaitingForInput;
        var next = inputs.Dequeue();
        if (next.Frame != CurrentFrame)
            throw new InvalidOperationException("Spectator input stream is not consecutive.");
        game.AdvanceFrame(CurrentFrame, next.Inputs, false);
        CurrentFrame++;
        return AdvanceStatus.Advanced;
    }

    public int AdvanceAvailable(bool drain = false)
    {
        int count = 0;
        while (count < options.SpectatorCatchUpFrames && AdvanceFrame(drain) == AdvanceStatus.Advanced)
            count++;
        return count;
    }

    public void Close()
    {
        if (stopped)
            return;
        stopped = true;
        host.Disconnect();
        inputs.Clear();
    }
}
