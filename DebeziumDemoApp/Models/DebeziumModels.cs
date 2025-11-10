using System.Text.Json.Serialization;

namespace DebeziumDemoApp.Models;

public class DebeziumChangeEvent
{
    [JsonPropertyName("payload")]
    public Payload Payload { get; set; } = new();

    public Dictionary<string, object>? Metadata { get; set; }
}

public class Payload
{
    [JsonPropertyName("before")]
    public Dictionary<string, object>? Before { get; set; }

    [JsonPropertyName("after")]
    public Dictionary<string, object>? After { get; set; }

    [JsonPropertyName("source")]
    public Source Source { get; set; } = new();

    [JsonPropertyName("op")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("ts_ms")]
    public long TimestampMs { get; set; }
}

public class Source
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("connector")]
    public string Connector { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ts_ms")]
    public long TimestampMs { get; set; }

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = string.Empty;

    [JsonPropertyName("db")]
    public string Database { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("table")]
    public string Table { get; set; } = string.Empty;

    [JsonPropertyName("txId")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("lsn")]
    public long Lsn { get; set; }
}

public class DatabaseChangeNotification
{
    public string Operation { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public Dictionary<string, object>? Before { get; set; }
    public Dictionary<string, object>? After { get; set; }
    public DateTime Timestamp { get; set; }
    public long Lsn { get; set; }
    public string TransactionId { get; set; } = string.Empty;
}