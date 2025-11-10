using DebeziumDemoApp.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace DebeziumDemoApp.Core.DataSources;

/// <summary>
/// SQL Server数据源实现，支持通过CDC或Change Tracking获取变更数据
/// </summary>
public class SQLServerDataSource : IDataSource
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SQLServerDataSource> _logger;
    private SqlConnection? _connection;
    private readonly string _connectionString;
    private readonly string _databaseName;
    private bool _isConnected;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listeningTask;
    private long _lastLsn;

    public string Name { get; }
    public DataSourceType Type => DataSourceType.SQLServer;
    public bool IsConnected => _isConnected && _connection != null;

    public event EventHandler<DatabaseChangeEventArgs>? OnChange;

    public SQLServerDataSource(
        string name,
        IConfiguration configuration,
        ILogger<SQLServerDataSource> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;

        _connectionString = _configuration.GetConnectionString(name)
                           ?? _configuration[$"DataSources:{name}:ConnectionString"]
                           ?? throw new ArgumentException($"Connection string for '{name}' not found");

        _databaseName = _configuration[$"DataSources:{name}:DatabaseName"]
                       ?? new SqlConnectionStringBuilder(_connectionString).InitialCatalog;
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

            _connection = new SqlConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);

            // 检查并启用CDC
            await SetupCDCAsync(cancellationToken);

            // 获取当前LSN
            _lastLsn = await GetCurrentLsnAsync();

            _isConnected = true;
            _logger.LogInformation("[SQLSERVER] Connected to SQL Server database: {Database}", _databaseName);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Failed to connect to SQL Server");
            await DisconnectAsync(cancellationToken);
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

            _cancellationTokenSource?.Cancel();

            if (_listeningTask != null)
            {
                await _listeningTask;
            }

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

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
        if (!_isConnected)
        {
            throw new InvalidOperationException("SQL Server data source is not connected");
        }

        await foreach (var change in GetTableChangesAsync(typeof(T).Name, filter, cancellationToken))
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
            throw new InvalidOperationException("SQL Server data source is not connected");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var changes = await GetChangesWithRetryAsync(tableName, cancellationToken);

            foreach (var change in changes)
            {
                if (filter == null || filter(change))
                {
                    yield return change;
                }
            }
        }
    }

    private async Task<IEnumerable<DatabaseChange>> GetChangesWithRetryAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        try
        {
            var changes = await GetCDCChangesAsync(tableName, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return changes;
        }
        catch (OperationCanceledException)
        {
            return Enumerable.Empty<DatabaseChange>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Error getting changes for table {Table}", tableName);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return Enumerable.Empty<DatabaseChange>();
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
                health.Message = "SQL Server connection is not open";
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
                health.Message = "SQL Server connection is healthy";

                // 获取版本信息
                cmd.CommandText = "SELECT @@VERSION as version";
                var version = await cmd.ExecuteScalarAsync();
                health.Metrics.Add("Version", version?.ToString() ?? "Unknown");

                // 检查CDC状态
                var cdcStatus = await CheckCDCStatusAsync();
                health.Metrics.Add("CDCEnabled", cdcStatus);
                health.Metrics.Add("DatabaseName", _databaseName);
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
            _logger.LogError(ex, "[SQLSERVER] Health check failed");
        }

        return await Task.FromResult(health);
    }

    private async Task SetupCDCAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 检查SQL Server版本是否支持CDC
            var version = await GetSQLServerVersionAsync();
            if (version.Major < 2008) // SQL Server 2008及以上支持CDC
            {
                _logger.LogWarning("[SQLSERVER] SQL Server version {Version} does not support CDC", version);
                return;
            }

            // 检查数据库是否已启用CDC
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT is_cdc_enabled
                FROM sys.databases
                WHERE name = @databaseName";
            cmd.Parameters.AddWithValue("@databaseName", _databaseName);

            var cdcEnabled = (bool?)await cmd.ExecuteScalarAsync();

            if (!cdcEnabled.HasValue || !cdcEnabled.Value)
            {
                // 启用CDC
                cmd.CommandText = $@"
                    USE [{_databaseName}]
                    EXEC sys.sp_cdc_enable_db";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("[SQLSERVER] Enabled CDC for database: {Database}", _databaseName);
            }

            // 为关键表启用CDC
            var tables = new[] { "Products", "Orders", "Categories" };
            foreach (var table in tables)
            {
                await EnableTableCDCAsync(table, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SQLSERVER] Failed to setup CDC, will use Change Tracking instead");
            await SetupChangeTrackingAsync(cancellationToken);
        }
    }

    private async Task SetupChangeTrackingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cmd = _connection!.CreateCommand();

            // 启用变更跟踪
            cmd.CommandText = $@"
                ALTER DATABASE [{_databaseName}]
                SET CHANGE_TRACKING = ON
                (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON)";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // 为表启用变更跟踪
            var tables = new[] { "Products", "Orders", "Categories" };
            foreach (var table in tables)
            {
                cmd.CommandText = $@"
                    ALTER TABLE [{table}]
                    ENABLE CHANGE_TRACKING";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("[SQLSERVER] Enabled Change Tracking for database: {Database}", _databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Failed to setup Change Tracking");
        }
    }

    private async Task EnableTableCDCAsync(string tableName, CancellationToken cancellationToken)
    {
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = $@"
                USE [{_databaseName}]
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = '{tableName}')
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM sys.tables t
                                    JOIN sys.change_tracking_tables ct ON t.object_id = ct.object_id
                                    WHERE t.name = '{tableName}')
                    BEGIN
                        EXEC sys.sp_cdc_enable_table
                            @source_schema = 'dbo',
                            @source_name = '{tableName}',
                            @role_name = NULL,
                            @supports_net_changes = 1;
                    END
                END";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("[SQLSERVER] Enabled CDC for table: {Table}", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SQLSERVER] Failed to enable CDC for table: {Table}", tableName);
        }
    }

    private async Task<long> GetCurrentLsnAsync()
    {
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT sys.fn_cdc_get_max_lsn()";
            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<List<DatabaseChange>> GetCDCChangesAsync(string tableName, CancellationToken cancellationToken)
    {
        var changes = new List<DatabaseChange>();

        try
        {
            using var cmd = _connection!.CreateCommand();

            // 获取CDC变更
            cmd.CommandText = $@"
                SELECT
                    __$operation as operation,
                    __$start_lsn as start_lsn,
                    __$end_lsn as end_lsn,
                    __$seqval as seqval,
                    __$update_mask as update_mask,
                    *
                FROM cdc.dbo_{tableName}_CT
                WHERE __$start_lsn > @lastLsn
                ORDER BY __$start_lsn";

            cmd.Parameters.AddWithValue("@lastLsn", _lastLsn);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var operation = reader.GetInt32(reader.GetOrdinal("operation"));
                var change = new DatabaseChange
                {
                    Operation = MapOperation(operation),
                    Database = _databaseName,
                    Schema = "dbo",
                    Table = tableName,
                    Timestamp = DateTime.UtcNow,
                    Lsn = reader.GetInt64(reader.GetOrdinal("start_lsn"))
                };

                // 解析变更前后的数据
                ParseCDCData(reader, change, operation);

                changes.Add(change);

                // 更新最后的LSN
                _lastLsn = Math.Max(_lastLsn, change.Lsn ?? 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Error getting CDC changes for table {Table}", tableName);
        }

        return changes;
    }

    private void ParseCDCData(SqlDataReader reader, DatabaseChange change, int operation)
    {
        try
        {
            var beforeData = new Dictionary<string, object>();
            var afterData = new Dictionary<string, object>();

            // 获取所有列
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                if (columnName.StartsWith("__$")) continue; // 跳过CDC元数据列

                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);

                switch (operation)
                {
                    case 1: // Delete
                    case 3: // Update (before values)
                    case 5: // Update with filter (before values)
                        beforeData[columnName] = value ?? DBNull.Value;
                        break;

                    case 2: // Insert
                    case 4: // Update (after values)
                    case 6: // Update with filter (after values)
                        afterData[columnName] = value ?? DBNull.Value;
                        break;
                }
            }

            if (beforeData.Any())
                change.Before = beforeData;
            if (afterData.Any())
                change.After = afterData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLSERVER] Error parsing CDC data");
        }
    }

    private string MapOperation(int operation)
    {
        return operation switch
        {
            1 => "DELETE",
            2 => "INSERT",
            3 => "UPDATE",
            4 => "UPDATE",
            5 => "UPDATE",
            6 => "UPDATE",
            _ => "UNKNOWN"
        };
    }

    private async Task<bool> CheckCDCStatusAsync()
    {
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT is_cdc_enabled
                FROM sys.databases
                WHERE name = @databaseName";
            cmd.Parameters.AddWithValue("@databaseName", _databaseName);

            var result = await cmd.ExecuteScalarAsync();
            return result != null && (bool)result;
        }
        catch
        {
            return false;
        }
    }

    private async Task<Version> GetSQLServerVersionAsync()
    {
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT SERVERPROPERTY('ProductVersion') as version";
            var versionString = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";

            // 解析版本字符串
            var match = System.Text.RegularExpressions.Regex.Match(versionString, @"^(\d+)\.(\d+)");
            if (match.Success)
            {
                var major = int.Parse(match.Groups[1].Value);
                var minor = int.Parse(match.Groups[2].Value);
                return new Version(major, minor);
            }

            return new Version(2008, 0); // 默认版本
        }
        catch
        {
            return new Version(2008, 0);
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}