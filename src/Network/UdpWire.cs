using System.Net;
using System.Net.Sockets;

namespace FrogSmashers.Network;

internal sealed class UdpWire : IWire
{
    private readonly Socket socket;
    private readonly Queue<WireMessage> incoming = new();
    private readonly byte[] buffer = new byte[8192];
    public string? Error { get; private set; }

    public UdpWire(IPEndPoint bind)
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        socket.Bind(bind);
    }

    public void Poll()
    {
        try
        {
            for (int n = 0; n < 512 && socket.Poll(0, SelectMode.SelectRead); n++)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int length = socket.ReceiveFrom(buffer, ref from);
                if (length is > 0 and <= 8192)
                {
                    incoming.Enqueue(new WireMessage(from.ToString()!, buffer[..length]));
                }
            }
        }
        catch (SocketException ex)
            when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.ConnectionReset or SocketError.MessageSize
            ) { }
        catch (SocketException ex)
        {
            Error = ex.Message;
        }
    }

    public void Send(string address, byte[] data, bool reliable)
    {
        try
        {
            socket.SendTo(data, IPEndPoint.Parse(address));
        }
        catch (SocketException ex)
            when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.NoBufferSpaceAvailable) { }
        catch (SocketException ex)
        {
            Error = ex.Message;
        }
    }

    public bool Receive(out WireMessage message) => incoming.TryDequeue(out message);

    public void Dispose() => socket.Dispose();
}
