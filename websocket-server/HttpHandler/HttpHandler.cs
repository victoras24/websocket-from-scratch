using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace server;

public class HttpHandler(ConnectionHandler connectionHandler)
{
    private ConnectionHandler _connectionHandler = connectionHandler;
    public string NextLine = "\r\n";
    public HttpHandlerRequest HandleRequestHeader(string request)
    {
        HttpHandlerRequest httpRequest = new HttpHandlerRequest();
        var lines = request.Split(["\r\n", "\n"], StringSplitOptions.None);
        var firstLine = lines[0];
        var specialLine = firstLine.Split(" ");
            if (specialLine.Length >= 3)
            { 
                httpRequest.Method = specialLine[0];
                httpRequest.Uri =  specialLine[1];
                httpRequest.HttpVersion =  specialLine[2];
            }
            else
            {
                Console.WriteLine("Invalid request line");
            }
            
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue; 

            var headerParts = lines[i].Split(':', 2); 
            if (headerParts.Length == 2)
            {
                var key = headerParts[0].Trim();
                var value = headerParts[1].Trim();
                httpRequest.Headers.Add(key.ToLower(), value);
            }
        }
        return httpRequest;
    }

    public void CreateHttpResponseHeader(HttpHandlerRequest httpRequest)
    {
        const string magicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        using SHA1 sha1 = SHA1.Create();
        byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(httpRequest.Headers["sec-websocket-key"] + magicString));
        string acceptKey = Convert.ToBase64String(hashBytes);

        var response = httpRequest.HttpVersion + " " + "101" + " " + "Switching Protocols" + NextLine +
                   "Upgrade: websocket" + NextLine +
                   "Connection: Upgrade" + NextLine +
                   "Sec-WebSocket-Accept: " + acceptKey + NextLine +
                   NextLine;

        _connectionHandler.NetworkStream.Write(Encoding.UTF8.GetBytes(response), 0, response.Length);

        Console.WriteLine("Websocket connected, reading frames now!");
    }
}