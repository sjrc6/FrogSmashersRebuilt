using System.Diagnostics;

namespace FrogSmashers.Network;

internal readonly record struct WireMessage(string Source, byte[] Data);

internal readonly record struct WireStatistics(long Sent, long Received, long Retried, long Ignored, long Backpressure);

internal interface IWire : IDisposable
{
    string? Error { get; }
    string LocalAddress => "";
    long TimeMilliseconds => Stopwatch.GetElapsedTime(0).Ticks / TimeSpan.TicksPerMillisecond;
    WireStatistics Statistics => default;

    void SetPeers(IReadOnlyCollection<string> addresses) { }
    bool IsConnected(string address) => true;
    bool TakeDisconnected(out string address)
    {
        address = "";
        return false;
    }
    void Poll();
    void Send(string address, byte[] data, bool reliable);
    bool Receive(out WireMessage message);
}
