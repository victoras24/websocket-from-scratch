using System.Net.Sockets;
using System.Text;

namespace server;

public class ConnectionHandler(TcpClient tcpClient)
{
    private readonly List<byte> _frameParserBuffer = new List<byte>();
    private readonly byte[] _readBuffer = new byte[8192];
    private int? _currentOpcode;
    private readonly List<byte> _messagePayload = new();
    public readonly NetworkStream NetworkStream = tcpClient.GetStream();
    public async Task RunWebsocketLoop()
    {
        while (true)
        {
            var bytesRead = await NetworkStream.ReadAsync(_readBuffer, 0, _readBuffer.Length);
            
            if (bytesRead == 0) break;

            _frameParserBuffer.AddRange(_readBuffer.Take(bytesRead));

            DecodeFrame();
        }
    }
    
    private bool DecodeFrame()
    {
        bool fin = (_frameParserBuffer[0] & 0b10000000) != 0;
        int opcode = _frameParserBuffer[0] & 0b00001111;
        bool masked = (_frameParserBuffer[1] & 0b10000000) != 0;
        int len = _frameParserBuffer[1] & 0b01111111;

        int headerSize = 2;
        if (len == 126) { len = (_frameParserBuffer[2] << 8) | _frameParserBuffer[3]; headerSize = 4; }
        else if (len == 127)
        {
            if (_frameParserBuffer.Count < 10) return false;

            len = (int)(
                ((ulong)_frameParserBuffer[2] << 56) |
                ((ulong)_frameParserBuffer[3] << 48) |
                ((ulong)_frameParserBuffer[4] << 40) |
                ((ulong)_frameParserBuffer[5] << 32) |
                ((ulong)_frameParserBuffer[6] << 24) |
                ((ulong)_frameParserBuffer[7] << 16) |
                ((ulong)_frameParserBuffer[8] << 8)  |
                (ulong)_frameParserBuffer[9]
            );
            headerSize = 10;
        }

        int maskOffset = headerSize;
        int payloadOffset = headerSize + (masked ? 4 : 0);
        int totalSize = payloadOffset + len;

        if (_frameParserBuffer.Count < totalSize)
            return false;

        byte[] payload = _frameParserBuffer.Skip(payloadOffset).Take(len).ToArray();

        if (masked)
        {
            byte[] key = _frameParserBuffer.Skip(maskOffset).Take(4).ToArray();
            UnmaskInPlace(payload, key);
        }
        
        // if (opcode >= 0x8)
        // {
        //     HandleControlFrame(opcode, payload);
        //     _frameParserBuffer.RemoveRange(0, totalSize);
        //     return true;
        // }

        if (_currentOpcode == null)
        {
            if (opcode != 0x1 && opcode != 0x2) return false;
            _currentOpcode = opcode;
        }
        else
        {
            if (opcode != 0x0)
            {
                Console.WriteLine("Protocol error: expected continuation frame");
                NetworkStream.Close();
                return false;
            }
        }

        _messagePayload.AddRange(payload);

        _frameParserBuffer.RemoveRange(0, totalSize);

        if (fin)
        {
            if (_currentOpcode == 0x1)
            {
                string text = Encoding.UTF8.GetString(_messagePayload.ToArray());
                Console.WriteLine($"[TEXT]: {text}");
            }
            else if (_currentOpcode == 0x2)
            {
                Console.WriteLine($"[BINARY]: {_messagePayload.Count} bytes");
            }

            _currentOpcode = null;
            _messagePayload.Clear();
        }

        return true;
    }

    private void UnmaskInPlace(byte[] payload, byte[] key)
    {
        for (int i = 0; i < payload.Length; i++)
            payload[i] ^= key[i % 4];
    }
    
    // private void HandleControlFrame(int opcode, byte[] payload)
    // {
    //     switch (opcode)
    //     {
    //         case 0x8: // Close
    //             Console.WriteLine("Received Close frame");
    //             // Send close back, then close stream
    //             break;
    //         case 0x9: // Ping
    //             Console.WriteLine("Received Ping");
    //             break;
    //         case 0xA: // Pong
    //             Console.WriteLine("Received Pong");
    //             break;
    //     }
    // }
}