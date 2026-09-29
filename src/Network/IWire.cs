namespace FrogSmashers.Network;

internal readonly record struct WireMessage(string Source, byte[] Data);

internal interface IWire : IDisposable
{
    string? Error { get; }

    void Poll();
    void Send(string address, byte[] data, bool reliable);
    bool Receive(out WireMessage message);
}
