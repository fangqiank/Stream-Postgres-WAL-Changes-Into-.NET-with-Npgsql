using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace DebeziumDemoApp.Core.DataTargets;

/// <summary>
/// SQL Server数据目标实现
/// </summary>
public class SQLServerDataTarget : IDataTarget
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SQLServerDataTarget> _logger;
    private SqlConnection? _connection;
    private bool _isConnected;
    private readonly DataTargetStatistics _statistics;

    public string Name { get; }
    public DataTargetType Type => DataTargetType.SQLServer;
    public bool IsConnected => _isConnected && _connection?.State == System.Data.ConnectionState.Open;
    public HashSet<Type> SupportedTypes { get; } = new() { typeof(object) };

    public event EventHandler<DataWriteEventArgs>? OnWrite;

    public SQLServerDataTarget(
        string name,
        IConfiguration configuration,
        ILogger<SQLServerDataTarget> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;
        _statistics = new DataTargetStatistics();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isConnected)
            {
                _logger.LogWarning("[SQLSERVER_TARGET] Already connected to SQL Server");
                return;
            }

            var connectionString = _configuration[$"DataTargets:{Name}:ConnectionString"];
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException($"Connection string not found for data target: {Name}");
            }

            _connection = new SqlConnection(connectionString);
            await _connection.OpenAsync(cancellationToken);

            _isConnected = true;
            _logger.LogInformation("[SQLSERVER_TARGET] Connected to SQL Server");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER_TARGET] Failed to connect to SQL Server");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_isConnected)
            {
                return;
            }

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;

            _isConnected = false;
            _logger.LogInformation("[SQLSERVER_TARGET] Disconnected from SQL Server");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER_TARGET] Error during disconnect");
        }
    }

    public async Task<DataWriteResult> WriteChangeAsync(DatabaseChange change, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new DataWriteResult
        {
            Operation = change.Operation.ToLower() switch
            {
                "insert" => DataOperation.Insert,
                "update" => DataOperation.Update,
                "delete" => DataOperation.Delete,
                _ => DataOperation.Unknown
            }
        };

        try
        {
            _statistics.TotalWrites++;

            if (!_isConnected || _connection == null)
            {
                _logger.LogWarning("[SQLSERVER_TARGET] SQL Server target is not connected");
                result.Success = false;
                result.ErrorMessage = "SQL Server target is not connected";
                _statistics.FailedWrites++;
                return result;
            }

            var tableName = $"sync_{change.Table}";
            var afterData = change.After;

            if (afterData == null)
            {
                _logger.LogWarning("[SQLSERVER_TARGET] No data to write for table {Table}", change.Table);
                result.Success = false;
                result.ErrorMessage = "No data to write";
                _statistics.FailedWrites++;
                return result;
            }

            var columns = string.Join(", ", afterData.Keys);
            var values = string.Join(", ", afterData.Keys.Select(key => $"@{key}"));

            var query = change.Operation.ToLower() switch
            {
                "insert" => $"INSERT INTO {tableName} ({columns}) VALUES ({values})",
                "update" => $"UPDATE {tableName} SET {string.Join(", ", afterData.Keys.Select(key => $"{key}=@{key}"))}",
                "delete" => $"DELETE FROM {tableName}",
                _ => throw new NotSupportedException($"Operation {change.Operation} is not supported")
            };

            using var command = new SqlCommand(query, _connection);

            foreach (var kvp in afterData)
            {
                command.Parameters.AddWithValue($"@{kvp.Key}", kvp.Value?.ToString() ?? (object)DBNull.Value);
            }

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogDebug("[SQLSERVER_TARGET] {Operation} on {Table} affected {Rows} rows",
                change.Operation, tableName, rowsAffected);

            result.Success = rowsAffected > 0;
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _statistics.SuccessfulWrites++;
            _statistics.LastWriteTime = DateTime.UtcNow;

            // 触发写入事件
            OnWrite?.Invoke(this, new DataWriteEventArgs { Result = result, TargetName = Name });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER_TARGET] Failed to write change to SQL Server");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _statistics.FailedWrites++;
            return result;
        }
    }

      public async Task<BatchDataWriteResult> WriteBatchAsync<T>(
        IEnumerable<T> dataList,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class
    {
        var startTime = DateTime.UtcNow;
        var dataItems = dataList.ToList();
        var result = new BatchDataWriteResult
        {
            Operation = operation,
            TotalCount = dataItems.Count
        };

        try
        {
            _logger.LogInformation("[SQLSERVER_TARGET] Writing batch of {Count} items to SQL Server", dataItems.Count);

            foreach (var item in dataItems)
            {
                var writeResult = await WriteAsync(item, operation, cancellationToken);
                if (writeResult.Success)
                {
                    result.SuccessCount++;
                    result.SuccessResults.Add(writeResult);
                }
                else
                {
                    result.FailureCount++;
                    result.FailureResults.Add(writeResult);
                }
            }

            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("[SQLSERVER_TARGET] Batch write completed: {Success}/{Total} successful",
                result.SuccessCount, result.TotalCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER_TARGET] Failed to write batch to SQL Server");
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return result;
        }
    }

    public async Task<BatchDataWriteResult> WriteChangesBatchAsync(
        IEnumerable<DatabaseChange> changes,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var changeList = changes.ToList();
        var result = new BatchDataWriteResult
        {
            TotalCount = changeList.Count
        };

        try
        {
            _logger.LogInformation("[SQLSERVER_TARGET] Writing batch of {Count} changes to SQL Server", changeList.Count);

            foreach (var change in changeList)
            {
                var writeResult = await WriteChangeAsync(change, cancellationToken);
                if (writeResult.Success)
                {
                    result.SuccessCount++;
                    result.SuccessResults.Add(writeResult);
                }
                else
                {
                    result.FailureCount++;
                    result.FailureResults.Add(writeResult);
                }
            }

            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("[SQLSERVER_TARGET] Batch write completed: {Success}/{Total} successful",
                result.SuccessCount, result.TotalCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER_TARGET] Failed to write batch to SQL Server");
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return result;
        }
    }

    public async Task<DataWriteResult> WriteAsync<T>(
        T data,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class
    {
        var startTime = DateTime.UtcNow;
        var result = new DataWriteResult
        {
            Operation = operation,
            DataType = typeof(T)
        };

        try
        {
            _statistics.TotalWrites++;

            if (!_isConnected || _connection == null)
            {
                result.Success = false;
                result.ErrorMessage = "SQL Server target is not connected";
                _statistics.FailedWrites++;
                return result;
            }

            // 简化实现 - 在实际应用中需要根据T的类型动态生成SQL
            _logger.LogInformation("[SQLSERVER_TARGET] Writing data of type {Type} to SQL Server", typeof(T).Name);
            await Task.Delay(100, cancellationToken); // 模拟写入操作

            result.Success = true;
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _statistics.SuccessfulWrites++;
            _statistics.LastWriteTime = DateTime.UtcNow;

            OnWrite?.Invoke(this, new DataWriteEventArgs { Result = result, TargetName = Name });
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER_TARGET] Failed to write data to SQL Server");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _statistics.FailedWrites++;
            return result;
        }
    }

    public async Task<DataWriteResult> DeleteAsync<T>(
        object id,
        CancellationToken cancellationToken = default) where T : class
    {
        var startTime = DateTime.UtcNow;
        var result = new DataWriteResult
        {
            Operation = DataOperation.Delete,
            DataType = typeof(T),
            Id = id
        };

        try
        {
            _statistics.TotalWrites++;

            if (!_isConnected || _connection == null)
            {
                result.Success = false;
                result.ErrorMessage = "SQL Server target is not connected";
                _statistics.FailedWrites++;
                return result;
            }

            _logger.LogInformation("[SQLSERVER_TARGET] Deleting record of type {Type} with ID {Id}", typeof(T).Name, id);
            await Task.Delay(50, cancellationToken); // 模拟删除操作

            result.Success = true;
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _statistics.SuccessfulWrites++;
            _statistics.LastWriteTime = DateTime.UtcNow;

            OnWrite?.Invoke(this, new DataWriteEventArgs { Result = result, TargetName = Name });
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER_TARGET] Failed to delete record from SQL Server");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.ExecutionTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            _statistics.FailedWrites++;
            return result;
        }
    }

    public async Task<DataTargetStatistics> GetStatisticsAsync()
    {
        return await Task.FromResult(_statistics);
    }

    public async Task ResetStatisticsAsync()
    {
        _statistics.Reset();
        await Task.CompletedTask;
    }

    public async Task<DataTargetHealth> CheckHealthAsync()
    {
        var health = new DataTargetHealth
        {
            LastCheck = DateTime.UtcNow
        };

        try
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                health.IsHealthy = false;
                health.Status = "Disconnected";
                health.Message = "SQL Server connection is not established";
                return health;
            }

            // 简单的健康检查查询
            using var command = new SqlCommand("SELECT 1", _connection);
            await command.ExecuteScalarAsync();

            health.IsHealthy = true;
            health.Status = "Connected";
            health.Message = "SQL Server target is healthy";
            health.Metrics.Add("Database", _connection.Database);
            health.Metrics.Add("DataSource", _connection.DataSource);
            health.Metrics.Add("ServerVersion", _connection.ServerVersion);
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[SQLSERVER_TARGET] Health check failed");
        }

        return await Task.FromResult(health);
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}