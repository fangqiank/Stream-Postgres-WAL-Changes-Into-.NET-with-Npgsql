using DebeziumDemoApp.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace DebeziumDemoApp.Core.DataSources;

/// <summary>
/// MongoDB数据源实现，简化版本（不包含变更流功能）
/// </summary>
public class MongoDBDataSource : IDataSource
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MongoDBDataSource> _logger;
    private IMongoDatabase? _database;
    private MongoClient? _client;
    private readonly string _connectionString;
    private readonly string _databaseName;
    private bool _isConnected;

    public string Name { get; }
    public DataSourceType Type => DataSourceType.MongoDB;
    public bool IsConnected => _isConnected && _client != null && _database != null;

    public event EventHandler<DatabaseChangeEventArgs>? OnChange;

    public MongoDBDataSource(
        string name,
        IConfiguration configuration,
        ILogger<MongoDBDataSource> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;

        _connectionString = _configuration.GetConnectionString(name)
                             ?? _configuration[$"DataSources:{name}:ConnectionString"]
                             ?? throw new ArgumentException($"Connection string for '{name}' not found");

        _databaseName = _configuration[$"DataSources:{name}:DatabaseName"]
                         ?? throw new ArgumentException($"Database name for '{name}' not found");
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _client = new MongoClient(_connectionString);
            _database = _client.GetDatabase(_databaseName);

            // Test the connection
            await _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            _isConnected = true;

            _logger.LogInformation("[MONGO] Connected successfully (change stream disabled)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MONGO] Failed to connect");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _client = null;
            _database = null;
            _isConnected = false;

            _logger.LogInformation("[MONGO] Disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MONGO] Error during disconnect");
        }
    }

    public async IAsyncEnumerable<DatabaseChange> GetChangesAsync<T>(
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("MongoDB data source is not connected");
        }

        _logger.LogInformation("[MONGO] GetChangesAsync called - using polling approach");

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
            throw new InvalidOperationException("MongoDB data source is not connected");
        }

        _logger.LogInformation("[MONGO] GetTableChangesAsync called for collection: {TableName}", tableName);

        // 简单的轮询实现
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
        // 简单的轮询逻辑
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
            if (!_isConnected || _database == null)
            {
                health.Message = "Not connected";
                return health;
            }

            // Test database connectivity
            await _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: CancellationToken.None);

            var databaseStats = await _database.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1));
            health.Metrics.Add("Database", _databaseName);
            health.Metrics.Add("Collections", "Connected");

            health.IsHealthy = true;
            health.Status = "Healthy";
            health.Message = "Connection is healthy";
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[MONGO] Health check failed");
        }

        return health;
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}