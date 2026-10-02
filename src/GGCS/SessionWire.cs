using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GGCS;

internal sealed class SessionWire<TInput>
    where TInput : unmanaged
{
    private readonly IInputCodec<TInput> codec;
    private readonly ISpectatorInputCodec<TInput>? spectatorCodec;
    public Player[] Players { get; }
    public int InputSize => codec.Size;
    public int SpectatorInputSize => spectatorCodec?.Size ?? Players.Length * (InputSize + 1);
    public ulong ConfigurationId { get; }

    public SessionWire(
        IReadOnlyList<Player> players,
        IInputCodec<TInput> codec,
        SessionOptions options,
        string inputSchema,
        ISpectatorInputCodec<TInput>? spectatorCodec = null
    )
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(inputSchema);
        options.Validate();
        if (players.Count is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(players), "Sessions support 1 to 32 player inputs.");
        if (codec.Size is < 1 or > 128)
            throw new ArgumentOutOfRangeException(
                nameof(codec),
                "Input codecs must have a fixed size of 1 to 128 bytes."
            );
        Players = players.OrderBy(p => p.Handle).ToArray();
        for (int i = 0; i < Players.Length; i++)
            if (Players[i].Handle != i || Players[i].PeerId < 0)
                throw new ArgumentException(
                    "Player handles must be consecutive from zero, with nonnegative peer IDs.",
                    nameof(players)
                );
        this.codec = codec;
        this.spectatorCodec = spectatorCodec;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(options.FramesPerSecond);
        writer.Write(codec.Size);
        writer.Write(SpectatorInputSize);
        writer.Write(inputSchema);
        foreach (var player in Players)
        {
            writer.Write(player.Handle);
            writer.Write(player.PeerId);
        }
        ConfigurationId = BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(stream.ToArray()));
    }

    public byte[] Encode(ReadOnlySpan<TInput> inputs)
    {
        var bytes = new byte[inputs.Length * InputSize];
        for (int i = 0; i < inputs.Length; i++)
            codec.Encode(inputs[i], bytes.AsSpan(i * InputSize, InputSize));
        return bytes;
    }

    public TInput[] Decode(ReadOnlySpan<byte> bytes, int count)
    {
        if (bytes.Length != count * InputSize)
            throw new ArgumentException("Input bundle has an incorrect length.", nameof(bytes));
        var result = new TInput[count];
        for (int i = 0; i < count; i++)
            result[i] = codec.Decode(bytes.Slice(i * InputSize, InputSize));
        return result;
    }

    public byte[] EncodeSpectator(ReadOnlySpan<PlayerInput<TInput>> inputs)
    {
        var bytes = new byte[SpectatorInputSize];
        if (spectatorCodec != null)
        {
            spectatorCodec.Encode(inputs, bytes);
            return bytes;
        }
        for (int i = 0; i < Players.Length; i++)
        {
            bytes[i * (InputSize + 1)] = (byte)inputs[i].Status;
            codec.Encode(inputs[i].Input, bytes.AsSpan(i * (InputSize + 1) + 1, InputSize));
        }
        return bytes;
    }

    public PlayerInput<TInput>[] DecodeSpectator(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SpectatorInputSize)
            throw new ArgumentException("Spectator bundle has an incorrect length.", nameof(bytes));
        var inputs = new PlayerInput<TInput>[Players.Length];
        if (spectatorCodec != null)
        {
            spectatorCodec.Decode(bytes, inputs);
            return inputs;
        }
        for (int i = 0; i < inputs.Length; i++)
        {
            var status = (InputStatus)bytes[i * (InputSize + 1)];
            if (status is not (InputStatus.Confirmed or InputStatus.Disconnected))
                throw new ArgumentException("A spectator stream must only contain final inputs.", nameof(bytes));
            inputs[i] = new(codec.Decode(bytes.Slice(i * (InputSize + 1) + 1, InputSize)), status);
        }
        return inputs;
    }
}

internal sealed class SessionEvents
{
    private readonly Queue<SessionEvent> queue = new();

    public void Add(SessionEvent item)
    {
        if (queue.Count == 256)
            queue.Dequeue();
        queue.Enqueue(item);
    }

    public bool TryGet(out SessionEvent item) => queue.TryDequeue(out item);
}
