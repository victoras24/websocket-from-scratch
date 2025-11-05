using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using Timer = System.Timers.Timer;

namespace server;

public class ConnectionHandler(TcpClient tcpClient)
{
    private readonly List<byte> _frameParserBuffer = new List<byte>();
    private readonly byte[] _readBuffer = new byte[8192];
    private int? _currentOpcode;
    private readonly List<byte> _messagePayload = new();
    public readonly NetworkStream NetworkStream = tcpClient.GetStream();
    private readonly CancellationTokenSource _cancellationToken = new();
    private Timer _timer;
    private static readonly ConcurrentBag<ConnectionHandler> _connections = new();

    private static List<Player> _mockData = new()
    {
        new Player { Label = "Player 1", Score = "75", Color = "red" },
        new Player { Label = "Player 2", Score = "50", Color = "orange" }
    };
    
    public async Task RunWebsocketLoop()
    {
        try
        {
            SetTimerToPing();
            
            _connections.Add(this);
            string jsonMessage = System.Text.Json.JsonSerializer.Serialize(_mockData);
            await BroadcastAsync(jsonMessage);
            while (!_cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await NetworkStream.ReadAsync(_readBuffer);
                
                if (bytesRead == 0) break;

                _frameParserBuffer.AddRange(_readBuffer.Take(bytesRead));

                await DecodeFrame();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    private async Task<bool> DecodeFrame()
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
        
        if (opcode >= 0x8)
        {
           await HandleControlFrame(opcode, payload);
            _frameParserBuffer.RemoveRange(0, totalSize);
            return true;
        }

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
                
                var updated = System.Text.Json.JsonSerializer.Deserialize<Player>(text);

                var player = _mockData.FirstOrDefault(p => p.Label == updated?.Label);
                if (player != null)
                {
                    if (updated != null) player.Score = updated.Score;
                    Console.WriteLine($"{player.Label} updated to {player.Score}");
                }

                string jsonMessage = System.Text.Json.JsonSerializer.Serialize(_mockData);
                await BroadcastAsync(jsonMessage);
                
                Console.WriteLine(text);
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
    
    private async Task HandleControlFrame(int opcode, byte[] payload)
    {
        switch (opcode)
        {
            case 0x8: 
                Console.WriteLine("Received Close frame");
                switch (payload.Length)
                {
                    case 2:
                        Console.WriteLine(BitConverter.ToUInt16(payload.Reverse().ToArray(), 0));
                        Dispose();
                        await NetworkStream.WriteAsync(payload);
                        NetworkStream.Close();
                        break;
                    case > 2:
                        Console.WriteLine(BitConverter.ToUInt16(payload.Take(2).Reverse().ToArray(), 0));
                        Console.WriteLine(Encoding.UTF8.GetString(payload.Skip(2).Take(payload.Length - 2).ToArray()));
                        Dispose();
                        await NetworkStream.WriteAsync(payload);
                        NetworkStream.Close();
                        break;
                    default:
                        Console.WriteLine("oops");
                        Dispose();
                        NetworkStream.Close();
                        break;
                }
                break;
            case 0x9:
                Console.WriteLine("Received Ping");
                break;
            case 0xA: 
                Console.WriteLine("Received Pong");
                break;
        }
    }

    private async Task SendPingAsync()
    {
        var frame = new List<byte>();
        var payload = new byte[] { 0xFF, 0xFE  };
        
        frame.Add(0b10001001);
        frame.Add((byte)payload.Length);
        frame.AddRange(payload);
        
        await NetworkStream.WriteAsync(frame.ToArray());
    }
    
    
    
    private  async Task BroadcastAsync(string message)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var frame = new List<byte>();
        frame.Add(0b10000001); 
        frame.Add((byte)payload.Length);
        frame.AddRange(payload);
        var frameBytes = frame.ToArray();
        
        foreach (var connection in _connections)
        {
            connection.NetworkStream.Write(frame.ToArray());
        }

    }



    private void SetTimerToPing()
    {
        _timer = new Timer(20000);
        _timer.Elapsed += async (sender, e) => await SendPingAsync();
        _timer.AutoReset = true;
        _timer.Enabled = true;
    }

    private void Dispose()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _cancellationToken?.Cancel();

        if (_connections.TryTake(out var connection))
        {
            Console.WriteLine("Removed connection");
        }
        else
        {
            Console.WriteLine("No connection to remove");
        };
    }
}