using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FrogSmashers.Network;

internal sealed class UdpWire : IWire
{
    private readonly Socket socket;
    private readonly DatagramReliability reliability;
    private readonly byte[] buffer = new byte[1233];
    private long backpressure;
    private long ignored;
    public string? Error { get; private set; }
    public string LocalAddress => socket.LocalEndPoint!.ToString()!;
    public long TimeMilliseconds => Stopwatch.GetElapsedTime(0).Ticks / TimeSpan.TicksPerMillisecond;
    public WireStatistics Statistics
    {
        get
        {
            var statistics = reliability.Statistics;
            return statistics with
            {
                Backpressure = statistics.Backpressure + backpressure,
                Ignored = statistics.Ignored + ignored,
            };
        }
    }

    public UdpWire(IPEndPoint bind)
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        socket.Bind(bind);
        reliability = new DatagramReliability(Transmit, () => TimeMilliseconds);
    }

    public void SetPeers(IReadOnlyCollection<string> addresses) => reliability.SetPeers(addresses);

    public bool IsConnected(string address) => reliability.IsConnected(address);

    public bool TakeDisconnected(out string address) => reliability.TakeDisconnected(out address);

    public void Poll()
    {
        try
        {
            for (int n = 0; n < 512 && socket.Poll(0, SelectMode.SelectRead); n++)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int length = socket.ReceiveFrom(buffer, ref from);
                if (length is > 0 and <= 1232)
                    reliability.Process(from.ToString()!, buffer.AsSpan(0, length));
            }
        }
        catch (SocketException ex)
            when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset or SocketError.MessageSize
            ) { }
        catch (SocketException ex)
        {
            Error = ex.Message;
        }
        reliability.Poll();
    }

    public void Send(string address, byte[] data, bool reliable) => reliability.Send(address, data, reliable);

    private void Transmit(string address, byte[] data)
    {
        if (!IPEndPoint.TryParse(address, out var endpoint) || endpoint.AddressFamily != AddressFamily.InterNetwork)
        {
            ignored++;
            return;
        }
        try
        {
            socket.SendTo(data, endpoint);
        }
        catch (SocketException ex)
            when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable)
        {
            backpressure++;
        }
        catch (SocketException ex)
        {
            Error = ex.Message;
        }
    }

    public bool Receive(out WireMessage message) => reliability.Receive(out message);

    public void Dispose() => socket.Dispose();
}
