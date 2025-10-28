using System.Net.Sockets;
using System.Text;

namespace server;

public class ConnectionHandler(TcpClient tcpClient)
{
    public readonly byte[] FrameBuffer = new byte[1024];
    public readonly List<byte> MessageBuffer = new List<byte>();
    public readonly NetworkStream NetworkStream = tcpClient.GetStream();
    public async Task<string> HandleTcpClient()
    {
        var reader = await NetworkStream.ReadAsync(FrameBuffer, 0, FrameBuffer.Length);
        return Encoding.UTF8.GetString(FrameBuffer, 0, reader).Trim();
    }
}