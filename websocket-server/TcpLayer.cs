using System.Net;
using System.Net.Sockets;

namespace server;

public class TcpLayer(IPAddress address)
{
    private const int Port = 8080;
    private readonly byte[] _bytes = new byte[1024];
    private readonly TcpListener _tcpListener = new TcpListener(address, Port);
    
    public async Task<TcpClient> CreateTcpConnection()
    {
        _tcpListener.Start();

        while (true)
        {
            Console.WriteLine("Listening on port {0}", address + ":" + Port);
            var tcpClient = await _tcpListener.AcceptTcpClientAsync();
            return tcpClient;
        }
    }
}