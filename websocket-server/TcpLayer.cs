using System.Net;
using System.Net.Sockets;

namespace server;

public class TcpLayer(IPAddress address, int port)
{
    private readonly byte[] _bytes = new byte[1024];
    private readonly TcpListener _tcpListener = new TcpListener(address, port);
    
    public TcpClient CreateTcpConnection()
    {
        _tcpListener.Start();

        while (true)
        {
            Console.WriteLine("Listening on port {0}", address + ":" + port);
            var tcpClient = _tcpListener.AcceptTcpClient();
            return tcpClient;
        }
    }
}