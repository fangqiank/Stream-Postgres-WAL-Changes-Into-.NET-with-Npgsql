namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;

/// <summary>
/// PostgreSQL逻辑复制配置选项
/// </summary>
public class LogicalReplicationServiceOptions
{
    /// <summary>
    /// 是否启用逻辑复制服务
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 发布名称（在源数据库上）
    /// </summary>
    public string PublicationName { get; set; } = "neon_publication";

    /// <summary>
    /// 订阅名称（在目标数据库上）
    /// </summary>
    public string SubscriptionName { get; set; } = "local_subscription";

    /// <summary>
    /// 源数据库连接字符串
    /// </summary>
    public string SourceConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 目标数据库连接字符串
    /// </summary>
    public string TargetConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 复制槽名称
    /// </summary>
    public string ReplicationSlotName { get; set; } = "neon_replication_slot";

    /// <summary>
    /// 要复制的表名
    /// </summary>
    public List<string> TablesToReplicate { get; set; } = new()
    {
        "Orders",
        "OutboxEvents"
    };

    /// <summary>
    /// 启动延迟
    /// </summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 命令超时时间
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否自动创建发布和订阅
    /// </summary>
    public bool AutoCreatePublicationAndSubscription { get; set; } = true;

    /// <summary>
    /// 是否在启动时同步现有数据
    /// </summary>
    public bool CopyExistingDataOnStart { get; set; } = true;

    /// <summary>
    /// 重试间隔
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 10;
}

/// <summary>
/// 逻辑复制服务状态
/// </summary>
public class LogicalReplicationServiceStatus
{
    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 启动时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 运行时长
    /// </summary>
    public TimeSpan Uptime { get; set; }

    /// <summary>
    /// 订阅状态
    /// </summary>
    public string SubscriptionStatus { get; set; } = string.Empty;

    /// <summary>
    /// 最后错误信息
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// 复制延迟（字节）
    /// </summary>
    public long? ReplicationLagBytes { get; set; }

    /// <summary>
    /// 复制槽信息
    /// </summary>
    public string ReplicationSlotInfo { get; set; } = string.Empty;

    /// <summary>
    /// 已复制的消息数
    /// </summary>
    public long MessagesReplicated { get; set; }

    /// <summary>
    /// 错误计数
    /// </summary>
    public long ErrorCount { get; set; }
}