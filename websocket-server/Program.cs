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
                await tcpLayer.CreateTcpConnection();
        }

        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }
}
