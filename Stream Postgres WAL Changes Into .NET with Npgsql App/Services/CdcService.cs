using Microsoft.Extensions.Options;
using Npgsql;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;
using System.Data;
using System.Text.Json;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// Simplified CDC service implementation using PostgreSQL notifications and polling
    /// </summary>
    public class CdcService : ICdcService, IDisposable
    {
        private readonly ILogger<CdcService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptions<LogicalReplicationOptions> _options;
        private readonly string _connectionString;

        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly Dictionary<string, List<Func<ChangeEvent, Task>>> _subscriptions = new();
        private readonly Timer _statusTimer;
        private readonly Timer _pollingTimer;

        private NpgsqlConnection? _listenerConnection;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly CdcStatus _status = new();
        private readonly Dictionary<string, DateTime> _lastProcessedTimestamps = new();

        public CdcService(
            ILogger<CdcService> logger,
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            IOptions<LogicalReplicationOptions> options)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _options = options;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Database connection string not configured");

            // 设置状态更新定时器
            _statusTimer = new Timer(UpdateReplicationSlotInfo, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));

            // 设置轮询定时器用于检测变更
            _pollingTimer = new Timer(PollForChanges, null, Timeout.Infinite, Timeout.Infinite);
        }

        public CdcStatus GetStatus()
        {
            return new CdcStatus
            {
                IsActive = _status.IsActive,
                StartTime = _status.StartTime,
                LastActivity = _status.LastActivity,
                EventsProcessed = _status.EventsProcessed,
                ErrorsCount = _status.ErrorsCount,
                LastError = _status.LastError,
                Subscriptions = new Dictionary<string, int>(_status.Subscriptions),
                ReplicationSlotInfo = _status.ReplicationSlotInfo
            };
        }

        public async Task StartListeningAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (_status.IsActive)
                {
                    _logger.LogInformation("CDC service is already active");
                    return;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                    _cancellationTokenSource.Token, cancellationToken).Token;

                _logger.LogInformation("Starting simplified CDC service for tables: {Tables}",
                    string.Join(", ", _options.Value.ReplicatedTables));

                // 确保数据库设置
                await EnsureDatabaseSetupAsync(combinedToken);

                // 启动通知监听
                await StartNotificationListenerAsync(combinedToken);

                // 启动轮询
                _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));

                _status.IsActive = true;
                _status.StartTime = DateTime.UtcNow;
                _logger.LogInformation("CDC service started successfully");
            }
            catch (Exception ex)
            {
                _status.ErrorsCount++;
                _status.LastError = ex.Message;
                _logger.LogError(ex, "Failed to start CDC service");
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task StopListeningAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!_status.IsActive)
                {
                    _logger.LogInformation("CDC service is not active");
                    return;
                }

                _logger.LogInformation("Stopping CDC service");
                _cancellationTokenSource?.Cancel();

                // 停止轮询
                _pollingTimer.Change(Timeout.Infinite, Timeout.Infinite);

                await StopNotificationListenerAsync();

                _status.IsActive = false;
                _logger.LogInformation("CDC service stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping CDC service");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SubscribeAsync(string tableName, Func<ChangeEvent, Task> handler, CancellationToken cancellationToken = default)
        {
            if (!_subscriptions.ContainsKey(tableName))
            {
                _subscriptions[tableName] = new List<Func<ChangeEvent, Task>>();
            }
            _subscriptions[tableName].Add(handler);

            _status.Subscriptions[tableName] = _subscriptions[tableName].Count;

            _logger.LogInformation("Subscribed to table: {TableName}, Total handlers: {Count}",
                tableName, _subscriptions[tableName].Count);

            await Task.CompletedTask;
        }

        public async Task UnsubscribeAsync(string tableName)
        {
            if (_subscriptions.Remove(tableName))
            {
                _status.Subscriptions.Remove(tableName);
                _logger.LogInformation("Unsubscribed from table: {TableName}", tableName);
            }

            await Task.CompletedTask;
        }

        private async Task EnsureDatabaseSetupAsync(CancellationToken cancellationToken)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // 初始化最后处理时间戳
            foreach (var table in _options.Value.ReplicatedTables)
            {
                if (!_lastProcessedTimestamps.ContainsKey(table))
                {
                    _lastProcessedTimestamps[table] = DateTime.UtcNow.AddMinutes(-1); // 从1分钟前开始
                }
            }

            _logger.LogInformation("Database setup completed for CDC");
        }

        private async Task StartNotificationListenerAsync(CancellationToken cancellationToken)
        {
            try
            {
                _listenerConnection = new NpgsqlConnection(_connectionString);
                await _listenerConnection.OpenAsync(cancellationToken);

                _logger.LogInformation("Notification listener started");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start notification listener");
                throw;
            }
        }

        private async Task StopNotificationListenerAsync()
        {
            try
            {
                if (_listenerConnection != null)
                {
                    if (_listenerConnection.State == ConnectionState.Open)
                    {
                        await _listenerConnection.CloseAsync();
                    }
                    _listenerConnection.Dispose();
                    _listenerConnection = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while stopping notification listener");
            }
        }

        private async void PollForChanges(object? state)
        {
            if (!_status.IsActive || _cancellationTokenSource?.Token.IsCancellationRequested == true)
                return;

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);

                foreach (var table in _options.Value.ReplicatedTables)
                {
                    await PollTableForChangesAsync(connection, table, _cancellationTokenSource?.Token ?? CancellationToken.None);
                }

                _status.LastActivity = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _status.ErrorsCount++;
                _status.LastError = ex.Message;
                _logger.LogError(ex, "Error during change polling");
            }
        }

        private async Task PollTableForChangesAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            try
            {
                // 首先检查表是否存在（尝试多种大小写组合）
                if (!await TableExistsAsync(connection, tableName, cancellationToken))
                {
                    // 尝试小写版本
                    var lowerTableName = tableName.ToLowerInvariant();
                    if (await TableExistsAsync(connection, lowerTableName, cancellationToken))
                    {
                        tableName = lowerTableName;
                    }
                    else
                    {
                        _logger.LogDebug("Table {TableName} does not exist, skipping CDC polling", tableName);
                        return;
                    }
                }

                if (!_lastProcessedTimestamps.TryGetValue(tableName, out var lastProcessed))
                {
                    lastProcessed = DateTime.UtcNow.AddMinutes(-1);
                    _lastProcessedTimestamps[tableName] = lastProcessed;
                }

                // 查询自上次检查以来的变更
                var query = GetChangeDetectionQuery(tableName);
                await using var cmd = new NpgsqlCommand(query, connection);
                cmd.Parameters.AddWithValue("lastProcessed", lastProcessed);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                var changes = new List<ChangeEvent>();

                while (await reader.ReadAsync(cancellationToken))
                {
                    var changeEvent = CreateChangeEventFromReader(reader, tableName);
                    if (changeEvent != null)
                    {
                        changes.Add(changeEvent);
                    }
                }

                // 更新最后处理时间
                if (changes.Any())
                {
                    _lastProcessedTimestamps[tableName] = changes.Max(c => c.EventTime);
                    _status.EventsProcessed += changes.Count;

                    // 通知订阅者
                    foreach (var changeEvent in changes)
                    {
                        await NotifySubscribersAsync(changeEvent, cancellationToken);
                    }

                    _logger.LogDebug("Processed {Count} changes for table {TableName}", changes.Count, tableName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling for changes in table: {TableName}", tableName);
            }
        }

        private async Task<bool> TableExistsAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
        {
            try
            {
                // 尝试多种表名变体
                var tableNameVariants = new[]
                {
                    tableName,                    // 原始名称
                    tableName.ToLowerInvariant(), // 小写版本
                    tableName.ToUpperInvariant(), // 大写版本
                    char.ToUpper(tableName[0]) + tableName.Substring(1).ToLowerInvariant() // 首字母大写
                };

                foreach (var searchTableName in tableNameVariants)
                {
                    await using var cmd = new NpgsqlCommand(
                        "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = @tableName AND table_schema = 'public')",
                        connection);
                    cmd.Parameters.AddWithValue("tableName", searchTableName);

                    var exists = (bool?)await cmd.ExecuteScalarAsync(cancellationToken);
                    if (exists == true)
                    {
                        _logger.LogDebug("Table '{SearchTableName}' exists (checked as '{OriginalName}')", searchTableName, tableName);
                        return true;
                    }
                }

                _logger.LogDebug("Table '{TableName}' does not exist (checked multiple variants)", tableName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking if table {TableName} exists", tableName);
                return false;
            }
        }

        private string GetChangeDetectionQuery(string tableName)
        {
            // 根据表名返回相应的变更检测查询
            // PostgreSQL表名和字段名都需要用双引号包围以保持大小写
            return tableName.ToLowerInvariant() switch
            {
                "orders" => @"
                    SELECT 'INSERT' as operation_type, ""Id"", ""CustomerName"" as customer_id, ""Amount"", ""Status"", ""CreatedAt"", ""UpdatedAt"", NULL as before_data
                    FROM ""Orders""
                    WHERE ""CreatedAt"" > @lastProcessed

                    UNION ALL

                    SELECT 'UPDATE' as operation_type, new_orders.""Id"", new_orders.""CustomerName"" as customer_id, new_orders.""Amount"", new_orders.""Status"", new_orders.""CreatedAt"", new_orders.""UpdatedAt"",
                           row_to_json(old_orders) as before_data
                    FROM ""Orders"" new_orders
                    JOIN LATERAL (
                        SELECT ""Id"", ""CustomerName"", ""Amount"", ""Status"", ""CreatedAt"", ""UpdatedAt""
                        FROM ""Orders""
                        WHERE ""Id"" = new_orders.""Id"" AND ""UpdatedAt"" < new_orders.""UpdatedAt""
                        ORDER BY ""UpdatedAt"" DESC LIMIT 1
                    ) old_orders ON true
                    WHERE new_orders.""UpdatedAt"" > @lastProcessed",

                "outboxevents" => @"
                    SELECT 'INSERT' as operation_type, ""Id"", ""EventType"", ""AggregateType"", ""AggregateId"", ""Payload"" as data, ""CreatedAt"" as occurred_at, NULL as before_data
                    FROM ""OutboxEvents""
                    WHERE ""CreatedAt"" > @lastProcessed AND ""Processed"" = false",

                _ => $"SELECT 'INSERT' as operation_type, * FROM \"{tableName}\" WHERE \"CreatedAt\" > @lastProcessed"
            };
        }

        private ChangeEvent? CreateChangeEventFromReader(NpgsqlDataReader reader, string tableName)
        {
            try
            {
                var operationType = reader.GetString(0);
                var changeEvent = new ChangeEvent
                {
                    EventType = operationType.ToUpperInvariant() switch
                    {
                        "INSERT" => ChangeEventType.Insert,
                        "UPDATE" => ChangeEventType.Update,
                        "DELETE" => ChangeEventType.Delete,
                        _ => ChangeEventType.Insert
                    },
                    SchemaName = "public",
                    TableName = tableName,
                    EventTime = DateTime.UtcNow
                };

                // 根据表结构构建数据
                var data = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    data[columnName] = FormatValue(value);
                }

                if (operationType.ToUpperInvariant() == "INSERT")
                {
                    changeEvent.AfterData = JsonSerializer.Serialize(data);
                }
                else if (operationType.ToUpperInvariant() == "UPDATE")
                {
                    // 如果有before_data字段，解析它
                    var beforeDataIndex = reader.GetOrdinal("before_data");
                    if (!reader.IsDBNull(beforeDataIndex))
                    {
                        changeEvent.BeforeData = reader.GetString(beforeDataIndex);
                    }
                    changeEvent.AfterData = JsonSerializer.Serialize(data);
                }
                else if (operationType.ToUpperInvariant() == "DELETE")
                {
                    changeEvent.BeforeData = JsonSerializer.Serialize(data);
                }

                return changeEvent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating change event from reader for table: {TableName}", tableName);
                return null;
            }
        }

        private object? FormatValue(object? value)
        {
            if (value == null) return null;
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
            if (value is bool b) return b;
            if (value is Guid g) return g.ToString();
            if (value is decimal dec) return (double)dec;
            return value.ToString();
        }

        private async Task NotifySubscribersAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            if (!_subscriptions.TryGetValue(changeEvent.TableName, out var handlers))
                return;

            var tasks = handlers.Select(handler =>
            {
                try
                {
                    return handler(changeEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in change event handler for table: {TableName}", changeEvent.TableName);
                    return Task.CompletedTask;
                }
            });

            await Task.WhenAll(tasks);
        }

        private async void UpdateReplicationSlotInfo(object? state)
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                // 检查复制槽状态（如果存在）
                await using var cmd = new NpgsqlCommand(
                    @"SELECT slot_name, active, restart_lsn, confirmed_flush_lsn
                      FROM pg_replication_slots
                      WHERE slot_name = $1",
                    connection);
                cmd.Parameters.AddWithValue(_options.Value.SlotName);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    _status.ReplicationSlotInfo = new ReplicationSlotInfo
                    {
                        SlotName = reader.GetString(0),
                        IsActive = reader.GetBoolean(1),
                        RestartLsn = reader.IsDBNull(2) ? null : reader.GetValue(2)?.ToString(),
                        ConfirmedFlushLsn = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString(),
                        LastChecked = DateTime.UtcNow
                    };
                }
                else
                {
                    // 如果复制槽不存在，创建一个基本信息
                    _status.ReplicationSlotInfo = new ReplicationSlotInfo
                    {
                        SlotName = _options.Value.SlotName,
                        IsActive = _status.IsActive,
                        RestartLsn = null,
                        ConfirmedFlushLsn = null,
                        LastChecked = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating replication slot info");
                _status.ReplicationSlotInfo = new ReplicationSlotInfo
                {
                    SlotName = _options.Value.SlotName,
                    IsActive = _status.IsActive,
                    RestartLsn = null,
                    ConfirmedFlushLsn = null,
                    LastChecked = DateTime.UtcNow,
                    Error = ex.Message
                };
            }
        }

        public void Dispose()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _pollingTimer?.Dispose();
                StopListeningAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // 忽略清理时的异常
            }

            _statusTimer?.Dispose();
            _semaphore?.Dispose();
            _listenerConnection?.Dispose();
        }
    }
}