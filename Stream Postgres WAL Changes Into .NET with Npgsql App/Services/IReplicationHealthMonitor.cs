namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// 复制健康监控器接口
    /// </summary>
    public interface IReplicationHealthMonitor
    {
        /// <summary>
        /// 复制槽状态
        /// </summary>
        ReplicationSlotStatus? SlotStatus { get; }

        /// <summary>
        /// 是否健康
        /// </summary>
        bool IsHealthy { get; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        DateTime LastUpdated { get; }

        /// <summary>
        /// 复制延迟（毫秒）
        /// </summary>
        long ReplicationLagMs { get; }

        /// <summary>
        /// 更新复制槽状态
        /// </summary>
        Task UpdateSlotStatusAsync(string slotName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取健康状态详情
        /// </summary>
        Task<ReplicationHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 复制槽状态信息
    /// </summary>
    public class ReplicationSlotStatus
    {
        public string SlotName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string RestartLsn { get; set; } = string.Empty;
        public string ConfirmedFlushLsn { get; set; } = string.Empty;
        public string SlotType { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public bool IsTemporary { get; set; }
        public DateTime CheckedAt { get; set; }
        public long LagInBytes { get; set; }
    }

    /// <summary>
    /// 复制健康状态
    /// </summary>
    public class ReplicationHealthStatus
    {
        public bool IsHealthy { get; set; }
        public ReplicationSlotStatus? SlotStatus { get; set; }
        public long ReplicationLagMs { get; set; }
        public List<string> Issues { get; set; } = new();
        public DateTime LastChecked { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
    }
}