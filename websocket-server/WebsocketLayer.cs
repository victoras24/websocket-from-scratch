using System.Text;

namespace server;

public class WebsocketLayer
{
    public string HandleWebsocketLayer(ConnectionHandler connectionHandler)
    {
        List<byte> messageBuffer = new List<byte>();
        byte[] frameBuffer = new byte[1024];
        int bytesRead = connectionHandler.NetworkStream.Read(frameBuffer, 0, frameBuffer.Length);
        messageBuffer.AddRange(frameBuffer.Take(bytesRead));
        return DecodeFrame(messageBuffer);
    }
    
    private static string DecodeFrame(List<byte> frameBuffer)
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
            // maybe store in buffer?
        };
        
        // the 2nd byte is for the MASK and the payload length. The MASK just takes the first bit.
        bool isPayloadDataMasked = (frameBuffer[1] & 0b10000000) != 0;
        Console.WriteLine($"Is payload masked? {isPayloadDataMasked}");
        
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
            // or buffer until more data arrives
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

        return Encoding.UTF8.GetString(unmaskedPayload);
    }
}