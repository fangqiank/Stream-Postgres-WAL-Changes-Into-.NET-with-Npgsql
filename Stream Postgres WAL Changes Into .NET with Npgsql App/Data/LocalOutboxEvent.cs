using System.ComponentModel.DataAnnotations.Schema;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;

/// <summary>
/// Local OutboxEvent model that matches the database schema
/// This maps to the outbox_events table structure created by the Docker initialization
/// </summary>
public class LocalOutboxEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; } = 0;
    public string Status { get; set; } = "Pending";

    // Computed properties for compatibility with the original OutboxEvent interface
    [NotMapped]
    public string AggregateType
    {
        get
        {
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(EventData);
                if (data != null && data.TryGetValue("AggregateType", out var aggregateType))
                {
                    return aggregateType.ToString() ?? string.Empty;
                }
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    [NotMapped]
    public string AggregateId
    {
        get
        {
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(EventData);
                if (data != null && data.TryGetValue("AggregateId", out var aggregateId))
                {
                    return aggregateId.ToString() ?? string.Empty;
                }
                return Id.ToString();
            }
            catch
            {
                return Id.ToString();
            }
        }
    }

    [NotMapped]
    public string Payload => EventData;

    [NotMapped]
    public bool Processed => ProcessedAt.HasValue;
}