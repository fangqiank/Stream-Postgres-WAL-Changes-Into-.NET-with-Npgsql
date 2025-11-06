namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration
{
    /// <summary>
    /// 逻辑复制服务配置
    /// </summary>
    public class LogicalReplicationOptions
    {
        /// <summary>
        /// 复制槽名称
        /// </summary>
        public string SlotName { get; set; } = "order_events_slot";

        /// <summary>
        /// 发布名称
        /// </summary>
        public string PublicationName { get; set; } = "cdc_publication";

        /// <summary>
        /// 心跳间隔（秒）
        /// </summary>
        public int HeartbeatInterval { get; set; } = 30;

        /// <summary>
        /// 连接重试间隔（毫秒）
        /// </summary>
        public int RetryInterval { get; set; } = 5000;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 10;

        /// <summary>
        /// 连接超时（秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = 30;

        /// <summary>
        /// 命令超时（秒）
        /// </summary>
        public int CommandTimeout { get; set; } = 60;

        /// <summary>
        /// 是否启用WAL解码
        /// </summary>
        public bool EnableWalDecoding { get; set; } = true;

        /// <summary>
        /// WAL解码插件（默认pgoutput）
        /// </summary>
        public string WalDecoderPlugin { get; set; } = "pgoutput";

        /// <summary>
        /// 批处理大小
        /// </summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>
        /// 监控和健康检查间隔（秒）
        /// </summary>
        public int HealthCheckInterval { get; set; } = 60;

        /// <summary>
        /// 是否在启动时创建复制槽（如果不存在）
        /// </summary>
        public bool CreateSlotIfNotExists { get; set; } = true;

        /// <summary>
        /// 是否启用复制槽状态监控
        /// </summary>
        public bool EnableSlotMonitoring { get; set; } = true;

        /// <summary>
        /// 复制延迟阈值（毫秒），超过此值将记录警告
        /// </summary>
        public long ReplicationLagThreshold { get; set; } = 30000; // 30秒

        /// <summary>
        /// 需要复制的表列表
        /// </summary>
        public List<string> ReplicatedTables { get; set; } = new()
        {
            "Orders",
            "OutboxEvents"
        };
    }
}