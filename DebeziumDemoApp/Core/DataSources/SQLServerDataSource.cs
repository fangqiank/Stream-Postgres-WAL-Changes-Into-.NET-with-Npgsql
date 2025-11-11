using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace DebeziumDemoApp.Core.DataSources;

/// <summary>
/// SQL Server数据源实现 - 使用CDC (Change Data Capture)
/// </summary>
public class SQLServerDataSource : IDataSource
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SQLServerDataSource> _logger;
    private SqlConnection? _connection;
    private Timer? _pollingTimer;
    private readonly ConcurrentQueue<DatabaseChange> _changeQueue;
    private bool _isConnected;

    public string Name { get; }
    public DataSourceType Type => DataSourceType.SQLServer;
    public bool IsConnected => _isConnected && _connection?.State == System.Data.ConnectionState.Open;

    public event EventHandler<DatabaseChangeEventArgs>? OnChange;

    public SQLServerDataSource(
        string name,
        IConfiguration configuration,
        ILogger<SQLServerDataSource> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;
        _changeQueue = new ConcurrentQueue<DatabaseChange>();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isConnected)
            {
                _logger.LogWarning("[SQLSERVER] Already connected to SQL Server");
                return;
            }

            var connectionString = _configuration[$"DataSources:{Name}:ConnectionString"];
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException($"Connection string not found for data source: {Name}");
            }

            _connection = new SqlConnection(connectionString);
            await _connection.OpenAsync(cancellationToken);

            // 启动轮询监控
            var pollingInterval = _configuration.GetValue<int>($"DataSources:{Name}:PollingIntervalSeconds", 30);
            _pollingTimer = new Timer(PollForChanges, null, TimeSpan.FromSeconds(pollingInterval), TimeSpan.FromSeconds(pollingInterval));

            _isConnected = true;
            _logger.LogInformation("[SQLSERVER] Connected to SQL Server, polling interval: {Interval}s", pollingInterval);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Failed to connect to SQL Server");
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

            _pollingTimer?.Dispose();
            _pollingTimer = null;

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;

            _isConnected = false;
            _logger.LogInformation("[SQLSERVER] Disconnected from SQL Server");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Error during disconnect");
        }
    }

    public async IAsyncEnumerable<DatabaseChange> GetChangesAsync<T>(
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        var tableName = typeof(T).Name;
        await foreach (var change in GetTableChangesAsync(tableName, filter, cancellationToken))
        {
            yield return change;
        }
    }

    public async IAsyncEnumerable<DatabaseChange> GetTableChangesAsync(
        string tableName,
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _connection == null)
        {
            throw new InvalidOperationException("SQL Server data source is not connected");
        }

        // 使用轮询方式获取变更（简化实现）
        var lastCheckTime = DateTime.UtcNow.AddMinutes(-1);

        while (!cancellationToken.IsCancellationRequested)
        {
            IEnumerable<DatabaseChange> changes;
            try
            {
                // 查询最近1分钟的变更（简化实现）
                var query = $@"
                    SELECT
                        'UPDATE' as Operation,
                        '{tableName}' as TableName,
                        GETDATE() as Timestamp,
                        *
                    FROM {tableName}
                    WHERE modified_date > @LastCheckTime
                    ORDER BY modified_date DESC";

                using var command = new SqlCommand(query, _connection);
                command.Parameters.AddWithValue("@LastCheckTime", lastCheckTime);

                changes = new List<DatabaseChange>();
                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var change = CreateDatabaseChange(reader);
                    if (filter == null || filter(change))
                    {
                        ((List<DatabaseChange>)changes).Add(change);
                    }
                }

                lastCheckTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SQLSERVER] Error polling for changes in table {Table}", tableName);
                changes = new List<DatabaseChange>();
                await Task.Delay(10000, cancellationToken); // 出错时延长等待时间
            }

            foreach (var change in changes)
            {
                yield return change;
            }

            await Task.Delay(5000, cancellationToken); // 5秒轮询间隔
        }
    }

    public async Task<DataSourceHealth> CheckHealthAsync()
    {
        var health = new DataSourceHealth
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
            health.Message = "SQL Server connection is healthy";
            health.Metrics.Add("Database", _connection.Database);
            health.Metrics.Add("DataSource", _connection.DataSource);
            health.Metrics.Add("ServerVersion", _connection.ServerVersion);
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[SQLSERVER] Health check failed");
        }

        return await Task.FromResult(health);
    }

    private void PollForChanges(object? state)
    {
        if (!_isConnected || _connection == null)
        {
            return;
        }

        try
        {
            // 简化的轮询实现 - 在实际应用中应该使用CDC或变更跟踪
            _logger.LogDebug("[SQLSERVER] Polling for changes...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Error during change polling");
        }
    }

    private DatabaseChange CreateDatabaseChange(SqlDataReader reader)
    {
        var data = new Dictionary<string, object>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var value = reader.GetValue(i);
            data[columnName] = value == DBNull.Value ? null : value;
        }

        return new DatabaseChange
        {
            Operation = data.GetValueOrDefault("Operation")?.ToString() ?? "UPDATE",
            Database = _connection?.Database ?? "Unknown",
            Schema = "dbo",
            Table = data.GetValueOrDefault("TableName")?.ToString() ?? "Unknown",
            After = data,
            Timestamp = DateTime.UtcNow,
            Source = new Dictionary<string, object>
            {
                ["connector"] = "sqlserver",
                ["version"] = "1.0.0",
                ["name"] = Name
            }
        };
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}