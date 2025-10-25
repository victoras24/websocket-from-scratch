using System.Net.Sockets;
using System.Text;

namespace server;

public class ConnectionHandler(TcpClient tcpClient, byte[] buffer)
{
    public string HandleTcpClient()
    {
        var stream = tcpClient.GetStream();
        var reader = stream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, reader);
    }
}