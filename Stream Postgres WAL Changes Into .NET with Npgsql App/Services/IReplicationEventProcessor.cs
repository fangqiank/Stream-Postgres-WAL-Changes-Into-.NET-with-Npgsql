namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// 复制事件处理器接口
    /// </summary>
    public interface IReplicationEventProcessor
    {
        /// <summary>
        /// 处理复制事件
        /// </summary>
        Task ProcessEventAsync(ReplicationEvent replicationEvent, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量处理复制事件
        /// </summary>
        Task ProcessEventsBatchAsync(IEnumerable<ReplicationEvent> events, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取处理统计信息
        /// </summary>
        Task<EventProcessingStats> GetProcessingStatsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 复制事件
    /// </summary>
    public class ReplicationEvent
    {
        public string EventType { get; set; } = string.Empty; // INSERT, UPDATE, DELETE
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public Dictionary<string, object> OldValues { get; set; } = new();
        public Dictionary<string, object> NewValues { get; set; } = new();
        public DateTime EventTime { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string Lsn { get; set; } = string.Empty;
    }

    /// <summary>
    /// 事件处理统计信息
    /// </summary>
    public class EventProcessingStats
    {
        public long TotalEventsProcessed { get; set; }
        public long EventsProcessedLastHour { get; set; }
        public long EventsProcessedToday { get; set; }
        public double AverageProcessingTimeMs { get; set; }
        public int FailedEvents { get; set; }
        public DateTime LastProcessedEvent { get; set; }
        public Dictionary<string, long> EventsByType { get; set; } = new();
        public Dictionary<string, long> EventsByTable { get; set; } = new();
    }
}