using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace server;

public class Start
{
    public static void Main()
    {
        const int port = 8080;
        byte[] bytes = new byte[1024];
        
        try
        {
            var tcpLayer = new TcpLayer(IPAddress.Any, port);
            var client = tcpLayer.CreateTcpConnection();
            var connectionHandler = new ConnectionHandler(client, bytes);
            var websocketLayer = new WebsocketLayer();
            var request = connectionHandler.HandleTcpClient();
            var httpHandler = new HttpHandler(connectionHandler);
            var requestHeader = httpHandler.HandleRequestHeader(request);
            
            if (requestHeader.Headers.ContainsKey("upgrade") && 
                requestHeader.Headers.ContainsKey("connection")&&
                requestHeader.Headers["upgrade"].ToLower() == "websocket" && 
                requestHeader.Headers["connection"].ToLower() == "upgrade")
            {
                
                httpHandler.CreateHttpResponseHeader(requestHeader);
                
                while (client.Connected)
                {
                    var data = websocketLayer.HandleWebsocketLayer(connectionHandler);
                    Console.WriteLine(data);
                }
            }
            else
            {
                var response = requestHeader.HttpVersion + " " + "200" + " " + "OK" + httpHandler.NextLine + "Content-Type: " + "text/plain" + httpHandler.NextLine + "Hello from C# server!";
                connectionHandler.NetworkStream.Write(Encoding.UTF8.GetBytes(response), 0, response.Length);
                connectionHandler.NetworkStream.Close();
                client.Close();
            }
            
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }
}
