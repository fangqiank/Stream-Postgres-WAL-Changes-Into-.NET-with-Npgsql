namespace DebeziumDemoApp.Core.Interfaces;

/// <summary>
/// 通用数据目标接口，支持向各种数据源写入数据
/// </summary>
public interface IDataTarget
{
    /// <summary>
    /// 数据目标名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 数据目标类型
    /// </summary>
    DataTargetType Type { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 支持的数据类型
    /// </summary>
    HashSet<Type> SupportedTypes { get; }

    /// <summary>
    /// 写入单个数据项
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="data">数据对象</param>
    /// <param name="operation">操作类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>写入结果</returns>
    Task<DataWriteResult> WriteAsync<T>(
        T data,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 批量写入数据
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="dataList">数据列表</param>
    /// <param name="operation">操作类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>批量写入结果</returns>
    Task<BatchDataWriteResult> WriteBatchAsync<T>(
        IEnumerable<T> dataList,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 写入数据库变更
    /// </summary>
    /// <param name="change">数据库变更</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>写入结果</returns>
    Task<DataWriteResult> WriteChangeAsync(
        DatabaseChange change,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量写入数据库变更
    /// </summary>
    /// <param name="changes">变更列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>批量写入结果</returns>
    Task<BatchDataWriteResult> WriteChangesBatchAsync(
        IEnumerable<DatabaseChange> changes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除数据
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="id">数据ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除结果</returns>
    Task<DataWriteResult> DeleteAsync<T>(
        object id,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 连接到数据目标
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接任务</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开数据目标连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>断开连接任务</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 健康检查
    /// </summary>
    /// <returns>健康状态</returns>
    Task<DataTargetHealth> CheckHealthAsync();

    /// <summary>
    /// 获取统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    Task<DataTargetStatistics> GetStatisticsAsync();

    /// <summary>
    /// 重置统计信息
    /// </summary>
    Task ResetStatisticsAsync();

    /// <summary>
    /// 写入事件
    /// </summary>
    event EventHandler<DataWriteEventArgs>? OnWrite;
}

/// <summary>
/// 数据目标类型
/// </summary>
public enum DataTargetType
{
    Unknown = 0,
    PostgreSQL = 1,
    MySQL = 2,
    MongoDB = 3,
    SQLServer = 4,
    Redis = 5,
    Elasticsearch = 6,
    RabbitMQ = 7,
    Custom = 99
}

/// <summary>
/// 数据操作类型
/// </summary>
public enum DataOperation
{
    Unknown = 0,
    Insert = 1,
    Update = 2,
    Delete = 3,
    Upsert = 4
}

/// <summary>
/// 数据写入结果
/// </summary>
public class DataWriteResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 操作类型
    /// </summary>
    public DataOperation Operation { get; set; }

    /// <summary>
    /// 数据类型
    /// </summary>
    public Type? DataType { get; set; }

    /// <summary>
    /// 数据ID
    /// </summary>
    public object? Id { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 异常对象
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// 批量数据写入结果
/// </summary>
public class BatchDataWriteResult
{
    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 成功数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 操作类型
    /// </summary>
    public DataOperation Operation { get; set; }

    /// <summary>
    /// 成功结果列表
    /// </summary>
    public List<DataWriteResult> SuccessResults { get; set; } = new();

    /// <summary>
    /// 失败结果列表
    /// </summary>
    public List<DataWriteResult> FailureResults { get; set; } = new();

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 是否全部成功
    /// </summary>
    public bool IsAllSuccess => FailureCount == 0;

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount : 0;
}

/// <summary>
/// 数据目标健康状态
/// </summary>
public class DataTargetHealth
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime LastCheck { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// 数据目标统计信息
/// </summary>
public class DataTargetStatistics
{
    /// <summary>
    /// 总写入次数
    /// </summary>
    public long TotalWrites { get; set; }

    /// <summary>
    /// 成功写入次数
    /// </summary>
    public long SuccessfulWrites { get; set; }

    /// <summary>
    /// 失败写入次数
    /// </summary>
    public long FailedWrites { get; set; }

    /// <summary>
    /// 平均写入时间（毫秒）
    /// </summary>
    public double AverageWriteTimeMs { get; set; }

    /// <summary>
    /// 最后写入时间
    /// </summary>
    public DateTime? LastWriteTime { get; set; }

    /// <summary>
    /// 按操作类型的统计
    /// </summary>
    public Dictionary<DataOperation, long> OperationCounts { get; set; } = new();

    /// <summary>
    /// 按数据类型的统计
    /// </summary>
    public Dictionary<string, long> DataTypeCounts { get; set; } = new();

    /// <summary>
    /// 重置统计信息
    /// </summary>
    public void Reset()
    {
        TotalWrites = 0;
        SuccessfulWrites = 0;
        FailedWrites = 0;
        AverageWriteTimeMs = 0;
        LastWriteTime = null;
        OperationCounts.Clear();
        DataTypeCounts.Clear();
    }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalWrites > 0 ? (double)SuccessfulWrites / TotalWrites : 0;
}

/// <summary>
/// 数据写入事件参数
/// </summary>
public class DataWriteEventArgs : EventArgs
{
    public DataWriteResult Result { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string TargetName { get; set; } = string.Empty;
}