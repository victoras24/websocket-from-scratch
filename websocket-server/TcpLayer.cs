using System.Net;
using System.Net.Sockets;
using System.Text;

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
            var connectionHandler = new ConnectionHandler(tcpClient);
            var httpHandler = new HttpHandler(connectionHandler);
            var requestHeader = await httpHandler.ReadHttpRequest();

            if (requestHeader.Headers.ContainsKey("upgrade") && 
                requestHeader.Headers.ContainsKey("connection")&&
                requestHeader.Headers["upgrade"].ToLower() == "websocket" && 
                requestHeader.Headers["connection"].ToLower() == "upgrade")
            {
                httpHandler.CreateHttpResponseHeader(requestHeader);
                connectionHandler.RunWebsocketLoop();
            }
            else
            {
                var response = requestHeader.HttpVersion + " " + "200" + " " + "OK" + httpHandler.NextLine + "Content-Type: " + "text/plain" + httpHandler.NextLine + "Hello from C# server!";
                connectionHandler.NetworkStream.Write(Encoding.UTF8.GetBytes(response), 0, response.Length);
                connectionHandler.NetworkStream.Close();
                tcpClient.Close();
            }
        }
    }
}