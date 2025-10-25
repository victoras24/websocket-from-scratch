using System.Net.Sockets;
using System.Text;

namespace server;

public class ConnectionHandler(TcpClient tcpClient, byte[] buffer)
{
    public readonly NetworkStream NetworkStream = tcpClient.GetStream();
    public string HandleTcpClient()
    {
        var reader = NetworkStream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, reader).Trim();
    }
}