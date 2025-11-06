namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration
{
    /// <summary>
    /// CDC配置选项
    /// </summary>
    public class CdcOptions
    {
        /// <summary>
        /// 是否启用CDC
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// CDC服务启动延迟（秒）
        /// </summary>
        public int StartupDelay { get; set; } = 10;

        /// <summary>
        /// 事件处理超时（毫秒）
        /// </summary>
        public int EventProcessingTimeout { get; set; } = 30000;

        /// <summary>
        /// 最大并发事件处理器数量
        /// </summary>
        public int MaxConcurrentEventProcessors { get; set; } = 10;

        /// <summary>
        /// 事件重试次数
        /// </summary>
        public int EventRetryAttempts { get; set; } = 3;

        /// <summary>
        /// 事件重试延迟（毫秒）
        /// </summary>
        public int EventRetryDelay { get; set; } = 1000;

        /// <summary>
        /// 是否启用死信队列
        /// </summary>
        public bool EnableDeadLetterQueue { get; set; } = true;

        /// <summary>
        /// 死信队列表名
        /// </summary>
        public string DeadLetterQueueTable { get; set; } = "cdc_dead_letter_events";

        /// <summary>
        /// 是否记录所有事件详情
        /// </summary>
        public bool LogAllEvents { get; set; } = false;

        /// <summary>
        /// 性能监控间隔（秒）
        /// </summary>
        public int PerformanceMonitoringInterval { get; set; } = 60;

        /// <summary>
        /// 是否启用事件序列化到文件
        /// </summary>
        public bool EnableEventSerialization { get; set; } = false;

        /// <summary>
        /// 事件序列化目录
        /// </summary>
        public string EventSerializationDirectory { get; set; } = "cdc_events";

        /// <summary>
        /// 是否启用变更数据验证
        /// </summary>
        public bool EnableDataValidation { get; set; } = true;

        /// <summary>
        /// 事件过滤配置
        /// </summary>
        public EventFilterOptions EventFilters { get; set; } = new();
    }

    /// <summary>
    /// 事件过滤配置
    /// </summary>
    public class EventFilterOptions
    {
        /// <summary>
        /// 需要排除的表
        /// </summary>
        public List<string> ExcludedTables { get; set; } = new();

        /// <summary>
        /// 需要排除的事件类型
        /// </summary>
        public List<string> ExcludedEventTypes { get; set; } = new();

        /// <summary>
        /// 只包含的事件类型（空表示包含所有）
        /// </summary>
        public List<string> IncludedEventTypes { get; set; } = new();

        /// <summary>
        /// 是否启用字段过滤
        /// </summary>
        public bool EnableFieldFiltering { get; set; } = false;

        /// <summary>
        /// 需要排除的字段（按表名分组）
        /// </summary>
        public Dictionary<string, List<string>> ExcludedFields { get; set; } = new();

        /// <summary>
        /// 只包含的字段（按表名分组）
        /// </summary>
        public Dictionary<string, List<string>> IncludedFields { get; set; } = new();
    }
}