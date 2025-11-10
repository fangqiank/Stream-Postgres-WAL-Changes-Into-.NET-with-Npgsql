using DebeziumDemoApp.Core.Interfaces;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Diagnostics;

namespace DebeziumDemoApp.Core.DataTargets;

/// <summary>
/// PostgreSQL数据目标实现，支持向PostgreSQL数据库写入数据
/// </summary>
public class PostgreSQLDataTarget : IDataTarget
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgreSQLDataTarget> _logger;
    private NpgsqlConnection? _connection;
    private readonly string _connectionString;
    private bool _isConnected;
    private readonly DataTargetStatistics _statistics = new();

    public string Name { get; }
    public DataTargetType Type => DataTargetType.PostgreSQL;
    public bool IsConnected => _isConnected && _connection != null;
    public HashSet<Type> SupportedTypes { get; } = new();

    public event EventHandler<DataWriteEventArgs>? OnWrite;

    public PostgreSQLDataTarget(
        string name,
        IConfiguration configuration,
        ILogger<PostgreSQLDataTarget> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;

        _connectionString = _configuration.GetConnectionString(name)
                           ?? _configuration[$"DataTargets:{name}:ConnectionString"]
                           ?? throw new ArgumentException($"Connection string for '{name}' not found");

        // 添加支持的基本类型
        SupportedTypes.Add(typeof(object));
        SupportedTypes.Add(typeof(Dictionary<string, object>));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isConnected)
            {
                _logger.LogWarning("[POSTGRES_TARGET] Already connected to PostgreSQL target");
                return;
            }

            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);

            // 启用重试逻辑
            if (_connection.State == System.Data.ConnectionState.Open)
            {
                _isConnected = true;
                _logger.LogInformation("[POSTGRES_TARGET] Connected to PostgreSQL target database");
            }
            else
            {
                throw new InvalidOperationException("Failed to open connection to PostgreSQL");
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[POSTGRES_TARGET] Failed to connect to PostgreSQL target");
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
            _logger.LogInformation("[POSTGRES_TARGET] Disconnected from PostgreSQL target");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[POSTGRES_TARGET] Error during disconnect");
        }
    }

    public async Task<DataWriteResult> WriteAsync<T>(
        T data,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DataWriteResult
        {
            Operation = operation,
            DataType = typeof(T)
        };

        try
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("PostgreSQL data target is not connected");
            }

            // 如果是通用对象，尝试转换为字典
            if (data is Dictionary<string, object> dict)
            {
                var tableName = GetTableNameFromData(dict);
                await WriteDictionaryAsync(tableName, dict, operation, cancellationToken);
                result.Success = true;
                result.Id = dict.GetValueOrDefault("id");
            }
            else
            {
                // 处理强类型对象
                await WriteTypedObjectAsync(data, operation, cancellationToken);
                result.Success = true;
                result.Id = GetIdFromObject(data);
            }

            _statistics.TotalWrites++;
            _statistics.SuccessfulWrites++;
            _statistics.LastWriteTime = DateTime.UtcNow;

            if (!_statistics.OperationCounts.ContainsKey(operation))
            {
                _statistics.OperationCounts[operation] = 0;
            }
            _statistics.OperationCounts[operation]++;

            _logger.LogDebug("[POSTGRES_TARGET] Successfully wrote {Operation} {DataType}", operation, typeof(T).Name);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;

            _statistics.TotalWrites++;
            _statistics.FailedWrites++;

            _logger.LogError(ex, "[POSTGRES_TARGET] Failed to write {Operation} {DataType}", operation, typeof(T).Name);
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            // 更新平均写入时间
            if (_statistics.TotalWrites > 0)
            {
                _statistics.AverageWriteTimeMs =
                    (_statistics.AverageWriteTimeMs * (_statistics.TotalWrites - 1) + result.ExecutionTimeMs) / _statistics.TotalWrites;
            }

            // 触发写入事件
            OnWrite?.Invoke(this, new DataWriteEventArgs
            {
                Result = result,
                TargetName = Name
            });
        }

        return result;
    }

    public async Task<BatchDataWriteResult> WriteBatchAsync<T>(
        IEnumerable<T> dataList,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<DataWriteResult>();
        var dataListList = dataList.ToList();

        foreach (var data in dataListList)
        {
            var result = await WriteAsync(data, operation, cancellationToken);
            results.Add(result);
        }

        stopwatch.Stop();

        var batchResult = new BatchDataWriteResult
        {
            TotalCount = dataListList.Count,
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success),
            Operation = operation,
            SuccessResults = results.Where(r => r.Success).ToList(),
            FailureResults = results.Where(r => !r.Success).ToList(),
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds
        };

        _logger.LogInformation("[POSTGRES_TARGET] Batch write completed: {Success}/{Total} successful in {ElapsedMs}ms",
            batchResult.SuccessCount, batchResult.TotalCount, batchResult.ExecutionTimeMs);

        return batchResult;
    }

    public async Task<DataWriteResult> WriteChangeAsync(
        DatabaseChange change,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DataWriteResult
        {
            Operation = MapOperation(change.Operation),
            DataType = typeof(DatabaseChange)
        };

        try
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("PostgreSQL data target is not connected");
            }

            await ApplyDatabaseChangeAsync(change, cancellationToken);

            result.Success = true;
            result.Id = change.Lsn;

            _statistics.TotalWrites++;
            _statistics.SuccessfulWrites++;
            _statistics.LastWriteTime = DateTime.UtcNow;

            _logger.LogDebug("[POSTGRES_TARGET] Successfully applied change {Operation} on {Table}",
                change.Operation, change.Table);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;

            _statistics.TotalWrites++;
            _statistics.FailedWrites++;

            _logger.LogError(ex, "[POSTGRES_TARGET] Failed to apply change {Operation} on {Table}",
                change.Operation, change.Table);
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            OnWrite?.Invoke(this, new DataWriteEventArgs
            {
                Result = result,
                TargetName = Name
            });
        }

        return result;
    }

    public async Task<BatchDataWriteResult> WriteChangesBatchAsync(
        IEnumerable<DatabaseChange> changes,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<DataWriteResult>();
        var changesList = changes.ToList();

        // 尝试使用事务进行批量写入
        using var transaction = await _connection!.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var change in changesList)
            {
                var result = await WriteChangeAsync(change, cancellationToken);
                results.Add(result);

                if (!result.Success)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    break;
                }
            }

            if (results.All(r => r.Success))
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "[POSTGRES_TARGET] Batch write transaction failed");
        }

        stopwatch.Stop();

        var batchResult = new BatchDataWriteResult
        {
            TotalCount = changesList.Count,
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success),
            Operation = DataOperation.Unknown,
            SuccessResults = results.Where(r => r.Success).ToList(),
            FailureResults = results.Where(r => !r.Success).ToList(),
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds
        };

        _logger.LogInformation("[POSTGRES_TARGET] Batch change write completed: {Success}/{Total} successful in {ElapsedMs}ms",
            batchResult.SuccessCount, batchResult.TotalCount, batchResult.ExecutionTimeMs);

        return batchResult;
    }

    public async Task<DataWriteResult> DeleteAsync<T>(
        object id,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DataWriteResult
        {
            Operation = DataOperation.Delete,
            DataType = typeof(T),
            Id = id
        };

        try
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("PostgreSQL data target is not connected");
            }

            var tableName = GetTableNameFromType(typeof(T));
            var affectedRows = await DeleteByIdAsync(tableName, id, cancellationToken);

            result.Success = affectedRows > 0;

            if (result.Success)
            {
                _statistics.TotalWrites++;
                _statistics.SuccessfulWrites++;
                _statistics.LastWriteTime = DateTime.UtcNow;

                if (!_statistics.OperationCounts.ContainsKey(DataOperation.Delete))
                {
                    _statistics.OperationCounts[DataOperation.Delete] = 0;
                }
                _statistics.OperationCounts[DataOperation.Delete]++;

                _logger.LogDebug("[POSTGRES_TARGET] Successfully deleted {Type} with ID {Id}", typeof(T).Name, id);
            }
            else
            {
                result.ErrorMessage = "No rows affected";
                _statistics.TotalWrites++;
                _statistics.FailedWrites++;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;

            _statistics.TotalWrites++;
            _statistics.FailedWrites++;

            _logger.LogError(ex, "[POSTGRES_TARGET] Failed to delete {Type} with ID {Id}", typeof(T).Name, id);
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            OnWrite?.Invoke(this, new DataWriteEventArgs
            {
                Result = result,
                TargetName = Name
            });
        }

        return result;
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
                health.Message = "PostgreSQL connection is not open";
                return health;
            }

            // 执行简单查询测试连接
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT 1 as test";
            var result = await cmd.ExecuteScalarAsync();

            if (result != null && result.ToString() == "1")
            {
                health.IsHealthy = true;
                health.Status = "Connected";
                health.Message = "PostgreSQL target connection is healthy";

                // 获取统计信息
                health.Metrics.Add("TotalWrites", _statistics.TotalWrites);
                health.Metrics.Add("SuccessRate", _statistics.SuccessRate);
                health.Metrics.Add("AverageWriteTimeMs", _statistics.AverageWriteTimeMs);
                health.Metrics.Add("LastWriteTime", _statistics.LastWriteTime?.ToString() ?? "Never");
            }
            else
            {
                health.IsHealthy = false;
                health.Status = "Error";
                health.Message = "Health check query failed";
            }
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[POSTGRES_TARGET] Health check failed");
        }

        return await Task.FromResult(health);
    }

    public async Task<DataTargetStatistics> GetStatisticsAsync()
    {
        return await Task.FromResult(_statistics);
    }

    public async Task ResetStatisticsAsync()
    {
        _statistics.Reset();
        _logger.LogInformation("[POSTGRES_TARGET] Statistics reset");
        await Task.CompletedTask;
    }

    private async Task WriteDictionaryAsync(
        string tableName,
        Dictionary<string, object> data,
        DataOperation operation,
        CancellationToken cancellationToken)
    {
        var sql = operation switch
        {
            DataOperation.Insert => GenerateInsertSql(tableName, data),
            DataOperation.Update => GenerateUpdateSql(tableName, data),
            DataOperation.Upsert => GenerateUpsertSql(tableName, data),
            DataOperation.Delete => GenerateDeleteSql(tableName, data),
            _ => throw new ArgumentException($"Unsupported operation: {operation}")
        };

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 30;

        // 添加参数
        foreach (var kvp in data)
        {
            var paramName = $"@{kvp.Key}";
            var value = kvp.Value ?? (object)DBNull.Value;
            cmd.Parameters.AddWithValue(paramName, value);
        }

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WriteTypedObjectAsync<T>(T data, DataOperation operation, CancellationToken cancellationToken)
    {
        // 将强类型对象转换为字典
        var dict = new Dictionary<string, object>();
        var properties = typeof(T).GetProperties();

        foreach (var prop in properties)
        {
            var value = prop.GetValue(data);
            dict[prop.Name.ToLower()] = value ?? (object)DBNull.Value;
        }

        var tableName = GetTableNameFromType(typeof(T));
        await WriteDictionaryAsync(tableName, dict, operation, cancellationToken);
    }

    private async Task ApplyDatabaseChangeAsync(DatabaseChange change, CancellationToken cancellationToken)
    {
        var tableName = change.Table;

        switch (change.Operation.ToLower())
        {
            case "c":
            case "create":
            case "insert":
                if (change.After != null)
                {
                    await WriteDictionaryAsync(tableName, change.After, DataOperation.Insert, cancellationToken);
                }
                break;

            case "u":
            case "update":
                if (change.After != null)
                {
                    await WriteDictionaryAsync(tableName, change.After, DataOperation.Update, cancellationToken);
                }
                break;

            case "d":
            case "delete":
                if (change.Before != null && change.Before.ContainsKey("id"))
                {
                    await DeleteByIdAsync(tableName, change.Before["id"], cancellationToken);
                }
                break;

            default:
                throw new ArgumentException($"Unsupported operation: {change.Operation}");
        }
    }

    private async Task<int> DeleteByIdAsync(string tableName, object id, CancellationToken cancellationToken)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = $"DELETE FROM {tableName} WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.CommandTimeout = 30;

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private string GenerateInsertSql(string tableName, Dictionary<string, object> data)
    {
        var columns = string.Join(", ", data.Keys);
        var values = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        return $"INSERT INTO {tableName} ({columns}) VALUES ({values})";
    }

    private string GenerateUpdateSql(string tableName, Dictionary<string, object> data)
    {
        if (!data.ContainsKey("id"))
        {
            throw new ArgumentException("Update operation requires 'id' field");
        }

        var setClause = string.Join(", ", data.Where(kvp => kvp.Key != "id")
                                           .Select(kvp => $"{kvp.Key} = @{kvp.Key}"));
        return $"UPDATE {tableName} SET {setClause} WHERE id = @id";
    }

    private string GenerateUpsertSql(string tableName, Dictionary<string, object> data)
    {
        var columns = string.Join(", ", data.Keys);
        var values = string.Join(", ", data.Keys.Select(k => $"@{k}"));
        var updateClause = string.Join(", ", data.Where(kvp => kvp.Key != "id")
                                             .Select(kvp => $"{kvp.Key} = EXCLUDED.{kvp.Key}"));
        return $"INSERT INTO {tableName} ({columns}) VALUES ({values}) ON CONFLICT (id) DO UPDATE SET {updateClause}";
    }

    private string GenerateDeleteSql(string tableName, Dictionary<string, object> data)
    {
        if (!data.ContainsKey("id"))
        {
            throw new ArgumentException("Delete operation requires 'id' field");
        }

        return $"DELETE FROM {tableName} WHERE id = @id";
    }

    private string GetTableNameFromData(Dictionary<string, object> data)
    {
        // 尝试从数据中获取表名，如果没有则使用默认映射
        if (data.ContainsKey("table"))
        {
            return data["table"].ToString() ?? "unknown";
        }

        return "unknown";
    }

    private string GetTableNameFromType(Type type)
    {
        // 简单的类型到表名映射
        var typeName = type.Name.ToLower();
        return typeName switch
        {
            var name when name.Contains("product") => "products",
            var name when name.Contains("order") => "orders",
            var name when name.Contains("category") => "categories",
            _ => typeName + "s" // 简单的复数化
        };
    }

    private object? GetIdFromObject<T>(T data)
    {
        var idProperty = typeof(T).GetProperty("Id");
        return idProperty?.GetValue(data);
    }

    private DataOperation MapOperation(string operation)
    {
        return operation.ToLower() switch
        {
            "c" or "create" or "insert" => DataOperation.Insert,
            "u" or "update" => DataOperation.Update,
            "d" or "delete" => DataOperation.Delete,
            _ => DataOperation.Unknown
        };
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}