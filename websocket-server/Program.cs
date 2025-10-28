using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace server;

public abstract class Start
{
    public static async Task Main()
    {
        try
        {
            var tcpLayer = new TcpLayer(IPAddress.Any);
            var client = await tcpLayer.CreateTcpConnection();
            var connectionHandler = new ConnectionHandler(client);
            var websocketLayer = new WebsocketLayer();
            var request = await connectionHandler.HandleTcpClient();
            var httpHandler = new HttpHandler(connectionHandler);
            var requestHeader = await httpHandler.HandleRequestHeader(request);

            if (requestHeader.Headers.ContainsKey("upgrade") && 
                requestHeader.Headers.ContainsKey("connection")&&
                requestHeader.Headers["upgrade"].ToLower() == "websocket" && 
                requestHeader.Headers["connection"].ToLower() == "upgrade")
            {
                httpHandler.CreateHttpResponseHeader(requestHeader);
                
                while (client.Connected)
                {
                    await websocketLayer.HandleWebsocketLayer(connectionHandler);
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
