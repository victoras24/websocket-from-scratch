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
        const string nextLine = "\r\n";
        byte[] bytes = new byte[1024];
        List<byte> messageBuffer = new List<byte>();
        
        TcpListener tcpListener = new TcpListener(IPAddress.Any, port);
        
        tcpListener.Start();
        Console.WriteLine("Listening on port {0}", IPAddress.Any + ":" + port);
        while (true)
        {
            var tcpClient = tcpListener.AcceptTcpClient();
            var stream = tcpClient.GetStream();
            var reader = stream.Read(bytes, 0, bytes.Length);
            if (reader == 0)
            {
                Console.WriteLine("Empty request received, skipping...");
                continue;
            }
            var stringByte = Encoding.UTF8.GetString(bytes, 0, reader);
            var data = stringByte.Trim();
            var httpRequest = new Dictionary<string, string>();
            var lines = data.Split(["\r\n", "\n"], StringSplitOptions.None);
            var firstLine = lines[0];
            var specialLine = firstLine.Split(" ");
            if (specialLine.Length >= 3)
            {
                httpRequest.Add("method", specialLine[0]);
                httpRequest.Add("uri", specialLine[1]);
                httpRequest.Add("httpVersion", specialLine[2]);
            }
            else
            {
                Console.WriteLine("Invalid request line");
                continue;
            }
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue; 

                var headerParts = lines[i].Split(':', 2); 
                if (headerParts.Length == 2)
                {
                    var key = headerParts[0].Trim();
                    var value = headerParts[1].Trim();
                    httpRequest.Add(key.ToLower(), value);
                }
            }

            foreach (var kv in httpRequest)
            {
                Console.WriteLine(kv.Key + ": " + kv.Value);
            }

            var response = "";
            
            if (httpRequest.ContainsKey("upgrade") && 
                httpRequest.ContainsKey("connection")&&
                httpRequest["upgrade"].ToLower() == "websocket" && 
                httpRequest["connection"].ToLower() == "upgrade")
            {
                // HTTP/1.1 101 Switching Protocols
                // Upgrade: websocket
                // Connection: Upgrade
                // Sec-WebSocket-Accept: <computed-value>
                // EMPTY LINE
                
                const string magicString = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
                using SHA1 sha1 = SHA1.Create();
                byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(httpRequest["sec-websocket-key"] + magicString));
                string acceptKey = Convert.ToBase64String(hashBytes);

                response = httpRequest["httpVersion"] + " " + "101" + " " + "Switching Protocols" + nextLine +
                           "Upgrade: websocket" + nextLine +
                           "Connection: Upgrade" + nextLine +
                           "Sec-WebSocket-Accept: " + acceptKey + nextLine +
                           nextLine;
                
                stream.Write(Encoding.UTF8.GetBytes(response), 0, response.Length);

                Console.WriteLine("Websocket connected, reading frames now!");

                while (tcpClient.Connected)
                {
                    byte[] frameBuffer = new byte[1024];
                    int bytesRead = stream.Read(frameBuffer, 0, frameBuffer.Length);
                    messageBuffer.AddRange(frameBuffer.Take(bytesRead));
                    DecodeFrame(messageBuffer);
                }
                
            }
            else
            {
                response = httpRequest["httpVersion"] + " " + "200" + " " + "OK" + nextLine + "Content-Type: " + "text/plain" + nextLine + "Hello from C# server!";
                stream.Write(Encoding.UTF8.GetBytes(response), 0, response.Length);
                stream.Close();
                tcpClient.Close();
            }
        }
    }

    private static void DecodeFrame(List<byte> frameBuffer)
    {
        foreach (var t in frameBuffer)
        {
            Console.WriteLine(t);
        }
        // we need to find the FIN. The FIN it shows if this is the last frame or there are also more frames coming. If it's 0 means more messages will follow.
        // if it's 1 means this is the last one. The 2nd, 3rd and 4th bit is reserved, rarely used. And the last 4 bits are use for the opcodes.
        bool isFinalFrame = (frameBuffer[0] & 0b10000000) != 0;
        Console.WriteLine($"Is this the final frame? {isFinalFrame}");
        int opcode = (frameBuffer[0] & 0b00001111);
        if (opcode == 1)
        {
            string dataType = "text";
            Console.WriteLine($"The payload data type is {dataType} {opcode}");
        }
        else
        {
            Console.WriteLine($"Check for other opcode types {opcode}");
            return;
        };
        
        // the 2nd byte is for the MASK and the payload length. The MASK just takes the first bit.
        bool isPayloadDataMasked = (frameBuffer[1] & 0b10000000) != 0;
        Console.WriteLine($"Is payload masked? {isPayloadDataMasked}");
        
        // TODO: check also for the other 2 options when it's 126 or above 127.
        // if the payload data length is lower than 126 means the next 4 bytes are the masking key.
        int payloadDataLength = (frameBuffer[1] & 0b01111111);
        Console.WriteLine($"Payload data length is {payloadDataLength}");
        int startingPayloadFrame = 2;
        if (payloadDataLength < 126)
        {
            startingPayloadFrame = 2;
        } else if (payloadDataLength == 126)
        {
            payloadDataLength = frameBuffer[2] << 8 | frameBuffer[3];
            startingPayloadFrame = 4;
        } else if (payloadDataLength == 127)
        {
            payloadDataLength = (int)BitConverter.ToUInt64([
                frameBuffer[9], frameBuffer[8], frameBuffer[7], frameBuffer[6],
                frameBuffer[5], frameBuffer[4], frameBuffer[3], frameBuffer[2]
            ], 0);
            startingPayloadFrame = 10;
        }
        
        Console.WriteLine($"Payload data length is {payloadDataLength}");
        
        // Storing masking keys which are I think always 4 bytes
        byte[] maskingKey = new byte[]
        {
            frameBuffer[startingPayloadFrame],
            frameBuffer[startingPayloadFrame + 1],
            frameBuffer[startingPayloadFrame + 2],
            frameBuffer[startingPayloadFrame + 3]
        };

        // Storing payload data
        byte[] payloadData = new byte[payloadDataLength];

        if (frameBuffer.Count < startingPayloadFrame + (isPayloadDataMasked ? 4 : 0) + payloadDataLength)
        {
            Console.WriteLine("Not enough data received yet. Waiting for more...");
            return; // or buffer until more data arrives
        }
        
        try
        {
            for (int i = 0; i < payloadDataLength; i++)
            {
                payloadData[i] = frameBuffer[i + startingPayloadFrame + 4];
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
            
        // Unmasking the stored payload data using the masking keys
        byte[] unmaskedPayload = new byte[payloadDataLength];

        for (var i = 0; i < payloadDataLength; i++)
        {
            unmaskedPayload[i] = (byte)(payloadData[i] ^ maskingKey[i % 4]);
        }
        
        Console.WriteLine(Encoding.UTF8.GetString(unmaskedPayload));
    }
}
