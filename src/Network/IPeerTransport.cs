namespace FrogSmashers.Network;

public readonly record struct Datagram(int Peer, byte[] Data);

public interface IPeerTransport : IDisposable
{
    void Poll();
    void Send(int peer, ReadOnlySpan<byte> data);
    bool TryReceive(out Datagram datagram);
    string? Error { get; }

    long TimeMilliseconds => Environment.TickCount64;
}
