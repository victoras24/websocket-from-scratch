using System.Text.Json.Serialization;

namespace server;

public class Player
{
    [JsonPropertyName("label")]
    public string Label { get; set; }
    [JsonPropertyName("score")]
    public string Score { get; set; }
    [JsonPropertyName("color")]
    public string Color { get; set; }
}
