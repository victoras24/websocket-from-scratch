using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    private static readonly ConcurrentDictionary<Guid, ConnectionHandler> _connections = new();
    private readonly Guid id = Guid.NewGuid();
    private readonly SemaphoreSlim _writeLock = new(1, 1);


    private static List<Player> _mockData = new()
    {
        new Player { Label = "Item 1", Score = 0, Color = "red" },
        new Player { Label = "Item 2", Score = 0, Color = "orange" }
    };
    
    public async Task RunWebsocketLoop()
    {
        try
        {
            SetTimerToPing();
        
            var dataMessage = new
            {
                type = "players",
                data = _mockData
            };
            string jsonMessage = JsonSerializer.Serialize(dataMessage);
            await SendFrameAsync(CreateTextFrame(jsonMessage)); 
    
            _connections.TryAdd(id, this);
    
            var connectionsMessage = new
            {
                type = "connections",
                data = _connections.Values.Select(c => new { c.id })
            };
            await BroadcastAsync(JsonSerializer.Serialize(connectionsMessage));

            while (!_cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await NetworkStream.ReadAsync(_readBuffer);
        
                if (bytesRead == 0) break;

                _frameParserBuffer.AddRange(_readBuffer.Take(bytesRead));

                await DecodeFrame();
            }
        }
        catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionReset)
        {
            Console.WriteLine($"Connection {id} reset by peer");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Connection {id} error: {e.Message}");
        }
        finally
        {
            Dispose();
            try { NetworkStream.Close(); } catch { }
            Console.WriteLine($"Connection {id} closed");
        }
    }

    private byte[] CreateTextFrame(string message)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var frame = new List<byte> { 0b10000001 }; 
    
        if (payload.Length <= 125)
        {
            frame.Add((byte)payload.Length);
        }
        else if (payload.Length <= 65535)
        {
            frame.Add(126);
            frame.Add((byte)(payload.Length >> 8));
            frame.Add((byte)(payload.Length & 0xFF));
        }
        else
        {
            frame.Add(127);
            for (int i = 7; i >= 0; i--)
            {
                frame.Add((byte)((payload.Length >> (i * 8)) & 0xFF));
            }
        }
    
        frame.AddRange(payload);
        return frame.ToArray();
    }
    
    private async Task<bool> DecodeFrame()
    {
        
        if (_frameParserBuffer.Count < 2)
            return false;

        bool fin = (_frameParserBuffer[0] & 0b10000000) != 0;
        int opcode = _frameParserBuffer[0] & 0b00001111;
        bool masked = (_frameParserBuffer[1] & 0b10000000) != 0;
        int len = _frameParserBuffer[1] & 0b01111111;

        int headerSize = 2;
        if (len == 126)
        {
            if (_frameParserBuffer.Count < 4)
                return false;
            len = (_frameParserBuffer[2] << 8) | _frameParserBuffer[3];
            headerSize = 4;
        }
        else if (len == 127)
        {
            if (_frameParserBuffer.Count < 10)
                return false;
        }
        
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
            HandleControlFrame(opcode, payload);
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
                
                var updated = JsonSerializer.Deserialize<Player>(text);

                var player = _mockData.FirstOrDefault(p => p.Label == updated?.Label);
                if (player != null)
                {
                    if (updated != null) player.Score = updated.Score;
                    Console.WriteLine($"{player.Label} updated to {player.Score}");
                }

                string jsonMessage = JsonSerializer.Serialize(_mockData);
                await BroadcastAsync(jsonMessage);
                
                var connectionsMessage = new
                {
                    type = "connections",
                    data = _connections.Values.Select(c => new { c.id })
                };
                await BroadcastAsync(JsonSerializer.Serialize(connectionsMessage));
                
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
    
    private void HandleControlFrame(int opcode, byte[] payload)
    {
        switch (opcode)
        {
            case 0x8: 
                Console.WriteLine("Received Close frame");
                Dispose(); 
            
                try
                {
                    
                    var closeFrame = new List<byte> { 0b10001000 }; 
                    closeFrame.Add((byte)payload.Length);
                    closeFrame.AddRange(payload);
                    NetworkStream.Write(closeFrame.ToArray());
                }
                catch
                {
                    
                }
                finally
                {
                    NetworkStream.Close();
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
        try
        {
            if (!NetworkStream.CanWrite || _cancellationToken.IsCancellationRequested)
                return;
            
            var frame = new List<byte>();
            var payload = new byte[] { 0xFF, 0xFE };
        
            frame.Add(0b10001001);
            frame.Add((byte)payload.Length);
            frame.AddRange(payload);
        
            await SendFrameAsync(frame.ToArray());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ping failed: {ex.Message}");
            
            Dispose();
        }
    }

    private void SetTimerToPing()
    {
        _timer = new Timer(20000);
        _timer.Elapsed += async (sender, e) =>
        {
            try
            {
                await SendPingAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Timer callback error: {ex.Message}");
            }
        };
        _timer.AutoReset = true;
        _timer.Enabled = true;
    }

    private void Dispose()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _cancellationToken?.Cancel();
        _connections.TryRemove(id, out _);
    }
    
    
    private async Task BroadcastAsync(string message)
    {
        var frameBytes = CreateTextFrame(message);

        foreach (var connection in _connections.Values)
        {
            try
            {
                await connection.SendFrameAsync(frameBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send to connection: {ex.Message}");
            }
        }
    }

    private async Task SendFrameAsync(byte[] bytes)
    {
        await _writeLock.WaitAsync();
        try
        {
            await NetworkStream.WriteAsync(bytes);
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
}