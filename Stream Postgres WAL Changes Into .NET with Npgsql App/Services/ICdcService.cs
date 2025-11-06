using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// CDC service interface - Real Change Data Capture
    /// </summary>
    public interface ICdcService
    {
        /// <summary>
        /// 开始监听数据库变更
        /// </summary>
        Task StartListeningAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止监听
        /// </summary>
        Task StopListeningAsync();

        /// <summary>
        /// 获取CDC状态
        /// </summary>
        CdcStatus GetStatus();

        /// <summary>
        /// 订阅特定表的变更
        /// </summary>
        Task SubscribeAsync(string tableName, Func<ChangeEvent, Task> handler, CancellationToken cancellationToken = default);

        /// <summary>
        /// 取消订阅
        /// </summary>
        Task UnsubscribeAsync(string tableName);
    }

    /// <summary>
    /// CDC状态
    /// </summary>
    public class CdcStatus
    {
        public bool IsActive { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime LastActivity { get; set; }
        public long EventsProcessed { get; set; }
        public long ErrorsCount { get; set; }
        public string? LastError { get; set; }
        public Dictionary<string, int> Subscriptions { get; set; } = new();
        public ReplicationSlotInfo? ReplicationSlotInfo { get; set; }
    }

    /// <summary>
    /// Replication slot information
    /// </summary>
    public class ReplicationSlotInfo
    {
        public string SlotName { get; set; } = string.Empty;
        public string PublicationName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? RestartLsn { get; set; }
        public string? ConfirmedFlushLsn { get; set; }
        public DateTime LastChecked { get; set; }
        public string? Error { get; set; }
    }
}