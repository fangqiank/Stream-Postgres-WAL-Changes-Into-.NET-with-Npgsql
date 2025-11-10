namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;

/// <summary>
/// 监控配置选项
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// 是否启用监控
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 指标收集间隔
    /// </summary>
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 报告生成间隔
    /// </summary>
    public TimeSpan ReportInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 告警阈值
    /// </summary>
    public AlertThresholds AlertThresholds { get; set; } = new();

    /// <summary>
    /// 性能配置
    /// </summary>
    public MonitoringPerformanceOptions Performance { get; set; } = new();

    /// <summary>
    /// 日志配置
    /// </summary>
    public LoggingOptions Logging { get; set; } = new();

    /// <summary>
    /// 导出配置
    /// </summary>
    public ExportOptions Export { get; set; } = new();
}

/// <summary>
/// 告警阈值配置
/// </summary>
public class AlertThresholds
{
    /// <summary>
    /// 复制延迟阈值
    /// </summary>
    public TimeSpan ReplicationLag { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 错误率阈值（0-1）
    /// </summary>
    public double ErrorRate { get; set; } = 0.01; // 1%

    /// <summary>
    /// 最小吞吐量阈值
    /// </summary>
    public int ThroughputMinimum { get; set; } = 100;

    /// <summary>
    /// 内存使用阈值（MB）
    /// </summary>
    public int MemoryThreshold { get; set; } = 512;

    /// <summary>
    /// CPU使用率阈值（0-1）
    /// </summary>
    public double CpuThreshold { get; set; } = 0.8; // 80%

    /// <summary>
    /// 连接池使用率阈值（0-1）
    /// </summary>
    public double ConnectionPoolThreshold { get; set; } = 0.9; // 90%

    /// <summary>
    /// 磁盘使用率阈值（0-1）
    /// </summary>
    public double DiskUsageThreshold { get; set; } = 0.85; // 85%
}

/// <summary>
/// 性能监控配置
/// </summary>
public class MonitoringPerformanceOptions
{
    /// <summary>
    /// 最大内存使用（MB）
    /// </summary>
    public int MaxMemoryUsageMB { get; set; } = 512;

    /// <summary>
    /// 最大GC暂停时间（毫秒）
    /// </summary>
    public int MaxGcPauseMs { get; set; } = 100;

    /// <summary>
    /// 最大线程数
    /// </summary>
    public int MaxThreads { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    /// 是否启用GC监控
    /// </summary>
    public bool EnableGcMonitoring { get; set; } = true;

    /// <summary>
    /// 是否启用线程监控
    /// </summary>
    public bool EnableThreadMonitoring { get; set; } = true;

    /// <summary>
    /// 是否启用内存监控
    /// </summary>
    public bool EnableMemoryMonitoring { get; set; } = true;

    /// <summary>
    /// 性能分析采样率（0-1）
    /// </summary>
    public double ProfilingSampleRate { get; set; } = 0.1; // 10%
}

/// <summary>
/// 日志监控配置
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// 是否启用结构化日志
    /// </summary>
    public bool EnableStructuredLogging { get; set; } = true;

    /// <summary>
    /// 日志级别
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// 是否记录性能日志
    /// </summary>
    public bool EnablePerformanceLogging { get; set; } = true;

    /// <summary>
    /// 是否记录错误日志详情
    /// </summary>
    public bool EnableErrorDetails { get; set; } = true;

    /// <summary>
    /// 日志保留天数
    /// </summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// 最大日志文件大小（MB）
    /// </summary>
    public int MaxFileSizeMB { get; set; } = 100;

    /// <summary>
    /// 最大日志文件数量
    /// </summary>
    public int MaxFileCount { get; set; } = 10;
}

/// <summary>
/// 导出配置
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// 是否启用指标导出
    /// </summary>
    public bool EnableMetricExport { get; set; } = false;

    /// <summary>
    /// 导出格式
    /// </summary>
    public ExportFormat Format { get; set; } = ExportFormat.Json;

    /// <summary>
    /// 导出间隔
    /// </summary>
    public TimeSpan ExportInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 导出目录
    /// </summary>
    public string ExportDirectory { get; set; } = "metrics";

    /// <summary>
    /// 是否启用Prometheus导出
    /// </summary>
    public bool EnablePrometheus { get; set; } = false;

    /// <summary>
    /// Prometheus端口
    /// </summary>
    public int PrometheusPort { get; set; } = 9090;

    /// <summary>
    /// Prometheus端点路径
    /// </summary>
    public string PrometheusPath { get; set; } = "/metrics";

    /// <summary>
    /// 是否启用InfluxDB导出
    /// </summary>
    public bool EnableInfluxDB { get; set; } = false;

    /// <summary>
    /// InfluxDB连接字符串
    /// </summary>
    public string? InfluxDBConnectionString { get; set; }

    /// <summary>
    /// InfluxDB数据库名称
    /// </summary>
    public string InfluxDBDatabase { get; set; } = "cdc_metrics";
}

/// <summary>
/// 导出格式
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// JSON格式
    /// </summary>
    Json,

    /// <summary>
    /// CSV格式
    /// </summary>
    Csv,

    /// <summary>
    /// XML格式
    /// </summary>
    Xml,

    /// <summary>
    /// Prometheus格式
    /// </summary>
    Prometheus,

    /// <summary>
    /// InfluxDB行协议格式
    /// </summary>
    InfluxLine
}