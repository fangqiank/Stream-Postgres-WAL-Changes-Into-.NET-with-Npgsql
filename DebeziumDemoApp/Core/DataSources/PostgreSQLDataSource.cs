using DebeziumDemoApp.Core.Interfaces;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace DebeziumDemoApp.Core.DataSources;

/// <summary>
/// PostgreSQL数据源实现，简化版本（不包含复制功能）
/// </summary>
public class PostgreSQLDataSource : IDataSource
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgreSQLDataSource> _logger;
    private NpgsqlConnection? _connection;
    private readonly string _connectionString;
    private bool _isConnected;

    public string Name { get; }
    public DataSourceType Type => DataSourceType.PostgreSQL;
    public bool IsConnected => _isConnected && _connection != null;

    public event EventHandler<DatabaseChangeEventArgs>? OnChange;

    public PostgreSQLDataSource(
        string name,
        IConfiguration configuration,
        ILogger<PostgreSQLDataSource> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;

        _connectionString = _configuration.GetConnectionString(name)
                           ?? _configuration[$"DataSources:{name}:ConnectionString"]
                           ?? throw new ArgumentException($"Connection string for '{name}' not found");
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);
            _isConnected = true;

            _logger.LogInformation("[POSTGRES] Connected successfully (replication disabled)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[POSTGRES] Failed to connect");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_listeningTask != null)
            {
                await _listeningTask;
            }

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
            _isConnected = false;

            _logger.LogInformation("[POSTGRES] Disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[POSTGRES] Error during disconnect");
        }
    }

    private Task? _listeningTask;

    public async IAsyncEnumerable<DatabaseChange> GetChangesAsync<T>(
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("PostgreSQL data source is not connected");
        }

        _logger.LogInformation("[POSTGRES] GetChangesAsync called - using polling approach");

        // 简单的轮询实现
        await foreach (var change in GetPollingChangesAsync<T>(filter, cancellationToken))
        {
            yield return change;
        }
    }

    public async IAsyncEnumerable<DatabaseChange> GetTableChangesAsync(
        string tableName,
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("PostgreSQL data source is not connected");
        }

        _logger.LogInformation("[POSTGRES] GetTableChangesAsync called for table: {TableName}", tableName);

        // 简单的表轮询实现
        await foreach (var change in GetPollingTableChangesAsync(tableName, filter, cancellationToken))
        {
            yield return change;
        }
    }

    private async IAsyncEnumerable<DatabaseChange> GetPollingChangesAsync<T>(
        Func<DatabaseChange, bool>? filter,
        CancellationToken cancellationToken) where T : class
    {
        // 简单的轮询逻辑
        var change = new DatabaseChange
        {
            TransactionId = Guid.NewGuid().ToString(),
            Operation = "r", // read
            Table = typeof(T).Name,
            Timestamp = DateTime.UtcNow,
            After = new Dictionary<string, object>()
        };

        if (filter == null || filter(change))
        {
            yield return change;
        }

        await Task.Delay(1000, cancellationToken); // 简单延迟
    }

    private async IAsyncEnumerable<DatabaseChange> GetPollingTableChangesAsync(
        string tableName,
        Func<DatabaseChange, bool>? filter,
        CancellationToken cancellationToken)
    {
        // 简单的表轮询逻辑
        var change = new DatabaseChange
        {
            TransactionId = Guid.NewGuid().ToString(),
            Operation = "r", // read
            Table = tableName,
            Timestamp = DateTime.UtcNow,
            After = new Dictionary<string, object>()
        };

        if (filter == null || filter(change))
        {
            yield return change;
        }

        await Task.Delay(1000, cancellationToken); // 简单延迟
    }

    public async Task<DataSourceHealth> CheckHealthAsync()
    {
        var health = new DataSourceHealth
        {
            IsHealthy = false,
            Status = "Unknown",
            Message = string.Empty,
            LastCheck = DateTime.UtcNow,
            Metrics = new Dictionary<string, object>()
        };

        try
        {
            if (!_isConnected || _connection == null)
            {
                health.Message = "Not connected";
                return health;
            }

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT version()";
            var version = await cmd.ExecuteScalarAsync();
            health.Metrics.Add("Version", version?.ToString() ?? "Unknown");

            health.IsHealthy = true;
            health.Status = "Healthy";
            health.Message = "Connection is healthy";
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[POSTGRES] Health check failed");
        }

        return health;
    }

    
    private NpgsqlConnectionStringBuilder _connectionBuilder =>
        new NpgsqlConnectionStringBuilder(_connectionString);

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}