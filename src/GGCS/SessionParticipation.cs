namespace GGCS;

public sealed class SessionParticipation<TInput>(int authorityHandle, TInput initialInput, Func<TInput, uint> players)
    where TInput : unmanaged
{
    public int AuthorityHandle { get; } = authorityHandle;
    public TInput InitialInput { get; } = initialInput;
    public Func<TInput, uint> Players { get; } = players;
}

public interface ISpectatorInputCodec<TInput>
    where TInput : unmanaged
{
    int Size { get; }
    void Encode(ReadOnlySpan<PlayerInput<TInput>> inputs, Span<byte> destination);
    void Decode(ReadOnlySpan<byte> source, Span<PlayerInput<TInput>> inputs);
}
