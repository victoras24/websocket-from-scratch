using System.Net;

namespace server;

public abstract class Start
{
    public static async Task Main()
    {
        try
        {
            var tcpLayer = new TcpLayer(IPAddress.Any);
            await tcpLayer.CreateTcpConnection();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Fatal error: {e}");
        }
    }
}