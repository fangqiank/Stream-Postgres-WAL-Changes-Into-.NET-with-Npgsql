namespace DebeziumDemoApp.Core.Interfaces;

/// <summary>
/// 通用数据源接口，支持从各种数据源获取变更数据
/// </summary>
public interface IDataSource
{
    /// <summary>
    /// 数据源名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 数据源类型
    /// </summary>
    DataSourceType Type { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 获取变更数据流
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="filter">可选过滤器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>变更数据流</returns>
    IAsyncEnumerable<DatabaseChange> GetChangesAsync<T>(
        Func<DatabaseChange, bool>? filter = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 获取指定表的变更数据
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="filter">可选过滤器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>变更数据流</returns>
    IAsyncEnumerable<DatabaseChange> GetTableChangesAsync(
        string tableName,
        Func<DatabaseChange, bool>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接到数据源
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接任务</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开数据源连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>断开连接任务</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 健康检查
    /// </summary>
    /// <returns>健康状态</returns>
    Task<DataSourceHealth> CheckHealthAsync();

    /// <summary>
    /// 变更数据事件
    /// </summary>
    event EventHandler<DatabaseChangeEventArgs>? OnChange;
}

/// <summary>
/// 数据源类型
/// </summary>
public enum DataSourceType
{
    Unknown = 0,
    Kafka = 1,
    PostgreSQL = 2,
    MySQL = 3,
    MongoDB = 4,
    SQLServer = 5,
    Redis = 6,
    RabbitMQ = 7,
    Custom = 99
}

/// <summary>
/// 数据源健康状态
/// </summary>
public class DataSourceHealth
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime LastCheck { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// 数据库变更事件参数
/// </summary>
public class DatabaseChangeEventArgs : EventArgs
{
    public DatabaseChange Change { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string SourceName { get; set; } = string.Empty;
}

/// <summary>
/// 通用数据库变更数据结构
/// </summary>
public class DatabaseChange
{
    /// <summary>
    /// 操作类型 (INSERT, UPDATE, DELETE)
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// 数据库名称
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// 模式名称
    /// </summary>
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// 表名
    /// </summary>
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// 变更前数据
    /// </summary>
    public Dictionary<string, object>? Before { get; set; }

    /// <summary>
    /// 变更后数据
    /// </summary>
    public Dictionary<string, object>? After { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 事务ID
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>
    /// LSN (日志序列号)
    /// </summary>
    public long? Lsn { get; set; }

    /// <summary>
    /// 源信息
    /// </summary>
    public Dictionary<string, object> Source { get; set; } = new();

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}