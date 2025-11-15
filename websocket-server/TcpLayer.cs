using System.Net;
using System.Net.Sockets;
using System.Text;

namespace server;

public class TcpLayer(IPAddress address)
{
    private const int Port = 8080;
    private readonly TcpListener _tcpListener = new TcpListener(address, Port);
    
    public async Task CreateTcpConnection()
    {
        _tcpListener.Start();
        Console.WriteLine("Server started on {0}:{1}", address, Port);
        
        while (true)
        {
            try
            {
                Console.WriteLine("Waiting for connection...");
                var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                Console.WriteLine("Client connected from {0}", tcpClient.Client.RemoteEndPoint);
                
                // Handle each connection on a separate task
                _ = Task.Run(async () => await HandleConnection(tcpClient));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting connection: {ex.Message}");
            }
        }
    }

    private async Task HandleConnection(TcpClient tcpClient)
    {
        ConnectionHandler? connectionHandler = null;
        try
        {
            connectionHandler = new ConnectionHandler(tcpClient);
            var httpHandler = new HttpHandler(connectionHandler);
            var requestHeader = await httpHandler.ReadHttpRequest();

            if (requestHeader == null)
            {
                Console.WriteLine("Empty request received, closing connection");
                tcpClient.Close();
                return;
            }

            if (requestHeader.Headers.ContainsKey("upgrade") && 
                requestHeader.Headers.ContainsKey("connection") &&
                requestHeader.Headers["upgrade"].ToLower() == "websocket" && 
                requestHeader.Headers["connection"].ToLower() == "upgrade")
            {
                httpHandler.CreateHttpResponseHeader(requestHeader);
                await connectionHandler.RunWebsocketLoop();
            }
            else
            {
                var response = requestHeader.HttpVersion + " 200 OK" + httpHandler.NextLine + 
                              "Content-Type: text/plain" + httpHandler.NextLine + 
                              httpHandler.NextLine + 
                              "Hello from C# server!";
                await connectionHandler.NetworkStream.WriteAsync(Encoding.UTF8.GetBytes(response));
                connectionHandler.NetworkStream.Close();
                tcpClient.Close();
            }
        }
        catch (IOException ex) when (ex.InnerException is SocketException se && 
                                      (se.SocketErrorCode == SocketError.ConnectionReset ||
                                       se.SocketErrorCode == SocketError.ConnectionAborted))
        {
            Console.WriteLine("Client disconnected during handshake");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling connection: {ex.Message}");
        }
        finally
        {
            tcpClient?.Close();
        }
    }
}