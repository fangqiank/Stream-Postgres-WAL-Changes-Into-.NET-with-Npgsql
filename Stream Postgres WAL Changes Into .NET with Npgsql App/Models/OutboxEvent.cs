namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models
{
    public class OutboxEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string AggregateType { get; set; } = string.Empty;
        public string AggregateId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool Processed { get; set; } = false;
        public  DateTime? ProcessedAt { get; set; }
    }
}
