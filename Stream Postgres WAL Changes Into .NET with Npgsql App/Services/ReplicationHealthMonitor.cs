using Microsoft.Extensions.Options;
using Npgsql;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using System.Data;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// 复制健康监控器实现
    /// </summary>
    public class ReplicationHealthMonitor : IReplicationHealthMonitor, IDisposable
    {
        private readonly ILogger<ReplicationHealthMonitor> _logger;
        private readonly IOptions<LogicalReplicationOptions> _options;
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly Timer? _monitoringTimer;

        public ReplicationSlotStatus? SlotStatus { get; private set; }
        public bool IsHealthy { get; private set; } = true;
        public DateTime LastUpdated { get; private set; } = DateTime.MinValue;
        public long ReplicationLagMs { get; private set; }

        public ReplicationHealthMonitor(
            ILogger<ReplicationHealthMonitor> logger,
            IConfiguration configuration,
            IOptions<LogicalReplicationOptions> options)
        {
            _logger = logger;
            _options = options;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Database connection string not configured");

            // 设置定时监控
            if (_options.Value.EnableSlotMonitoring)
            {
                _monitoringTimer = new Timer(
                    async _ => await UpdateSlotStatusAsync(_options.Value.SlotName),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(_options.Value.HealthCheckInterval));
            }
        }

        public async Task UpdateSlotStatusAsync(string slotName, CancellationToken cancellationToken = default)
        {
            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                _logger.LogWarning("复制槽状态更新超时");
                return;
            }

            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                // 查询复制槽状态
                await using var cmd = new NpgsqlCommand(
                    @"SELECT
                        slot_name,
                        active,
                        COALESCE(restart_lsn::text, 'N/A') as restart_lsn,
                        COALESCE(confirmed_flush_lsn::text, 'N/A') as confirmed_flush_lsn,
                        slot_type,
                        database,
                        temporary,
                        pg_wal_lsn_diff(pg_current_wal_lsn(), confirmed_flush_lsn) as lag_bytes
                        FROM pg_replication_slots
                        WHERE slot_name = $1",
                    connection);
                cmd.Parameters.AddWithValue(slotName);
                cmd.CommandTimeout = _options.Value.CommandTimeout;

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    var slotStatus = new ReplicationSlotStatus
                    {
                        SlotName = reader.GetString(0),
                        IsActive = reader.GetBoolean(1),
                        RestartLsn = reader.IsDBNull(2) ? "N/A" : reader.GetString(2),
                        ConfirmedFlushLsn = reader.IsDBNull(3) ? "N/A" : reader.GetString(3),
                        SlotType = reader.GetString(4),
                        Database = reader.GetString(5),
                        IsTemporary = reader.GetBoolean(6),
                        CheckedAt = DateTime.UtcNow,
                        LagInBytes = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
                    };

                    SlotStatus = slotStatus;
                    LastUpdated = DateTime.UtcNow;

                    // 计算复制延迟（简化估算）
                    ReplicationLagMs = EstimateReplicationLagMs(slotStatus.LagInBytes);

                    // 健康状态检查
                    await CheckHealthAsync(slotStatus, cancellationToken);

                    _logger.LogDebug("复制槽状态更新 - 名称: {SlotName}, 活跃: {Active}, 延迟: {LagMs}ms",
                        slotStatus.SlotName, slotStatus.IsActive, ReplicationLagMs);
                }
                else
                {
                    _logger.LogWarning("Replication slot {SlotName} does not exist", slotName);
                    IsHealthy = false;
                    LastUpdated = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新复制槽状态失败: {SlotName}", slotName);
                IsHealthy = false;
                LastUpdated = DateTime.UtcNow;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task CheckHealthAsync(ReplicationSlotStatus slotStatus, CancellationToken cancellationToken)
        {
            var issues = new List<string>();

            // 检查复制槽是否活跃
            if (!slotStatus.IsActive)
            {
                issues.Add("Replication slot is inactive");
                await DiagnoseInactiveSlotAsync(slotStatus.SlotName, cancellationToken);
            }

            // 检查复制延迟
            if (ReplicationLagMs > _options.Value.ReplicationLagThreshold)
            {
                issues.Add($"复制延迟过高: {ReplicationLagMs}ms");
            }

            // 检查复制槽状态
            if (slotStatus.RestartLsn == "N/A")
            {
                issues.Add("复制槽LSN状态异常");
            }

            // 检查数据库连接
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand("SELECT 1", connection);
                await cmd.ExecuteScalarAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                issues.Add($"数据库连接失败: {ex.Message}");
            }

            IsHealthy = !issues.Any();

            if (!IsHealthy)
            {
                _logger.LogWarning("Replication health check found issues: {Issues}", string.Join(", ", issues));
            }
        }

        private async Task DiagnoseInactiveSlotAsync(string slotName, CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                // 检查复制槽详细信息
                await using var cmd = new NpgsqlCommand(
                    @"SELECT
                        slot_name,
                        plugin,
                        slot_type,
                        database,
                        active,
                        active_pid,
                        xmin,
                        catalog_xmin,
                        restart_lsn,
                        confirmed_flush_lsn,
                        wal_status,
                        safe_wal_size
                        FROM pg_replication_slots
                        WHERE slot_name = $1",
                    connection);
                cmd.Parameters.AddWithValue(slotName);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var details = new
                    {
                        SlotName = reader.GetString(0),
                        Plugin = reader.GetString(1),
                        SlotType = reader.GetString(2),
                        Database = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                        IsActive = reader.GetBoolean(4),
                        ActivePid = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                        XMin = reader.IsDBNull(6) ? null : reader.GetValue(6)?.ToString(),
                        CatalogXMin = reader.IsDBNull(7) ? null : reader.GetValue(7)?.ToString(),
                        RestartLsn = reader.IsDBNull(8) ? "N/A" : reader.GetValue(8)?.ToString(),
                        ConfirmedFlushLsn = reader.IsDBNull(9) ? "N/A" : reader.GetValue(9)?.ToString(),
                        WalStatus = reader.IsDBNull(10) ? "N/A" : reader.GetString(10),
                        SafeWalSize = reader.IsDBNull(11) ? null : (long?)reader.GetInt64(11)
                    };

                    _logger.LogInformation("Replication slot details: {@SlotDetails}", details);

                    // 检查是否有活跃的复制进程
                    if (details.ActivePid.HasValue)
                    {
                        _logger.LogInformation("Replication slot {SlotName} has active process PID: {Pid}", slotName, details.ActivePid);
                    }
                    else
                    {
                        _logger.LogWarning("Replication slot {SlotName} has no active replication process", slotName);
                        await CheckReplicationProcessesAsync(cancellationToken);
                    }

                    // 检查WAL状态
                    if (details.WalStatus != "N/A")
                    {
                        _logger.LogInformation("Replication slot WAL status: {WalStatus}", details.WalStatus);
                    }
                }
                else
                {
                    _logger.LogWarning("Replication slot {SlotName} does not exist", slotName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "诊断复制槽 {SlotName} 时出错", slotName);
            }
        }

        private async Task CheckReplicationProcessesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                // 检查所有复制进程
                await using var cmd = new NpgsqlCommand(
                    @"SELECT
                        pid,
                        state,
                        application_name,
                        backend_start,
                        query,
                        wait_event_type,
                        wait_event
                        FROM pg_stat_activity
                        WHERE backend_type = 'walsender' OR query LIKE '%replication%'
                        ORDER BY backend_start",
                    connection);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                var processes = new List<object>();

                while (await reader.ReadAsync(cancellationToken))
                {
                    processes.Add(new
                    {
                        Pid = reader.GetInt32(0),
                        State = reader.GetString(1),
                        ApplicationName = reader.IsDBNull(2) ? null : reader.GetString(2),
                        BackendStart = reader.GetDateTime(3),
                        Query = reader.IsDBNull(4) ? null : reader.GetString(4),
                        WaitEventType = reader.IsDBNull(5) ? null : reader.GetString(5),
                        WaitEvent = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }

                if (processes.Any())
                {
                    _logger.LogInformation("Found {Count} replication processes: {@Processes}", processes.Count, processes);
                }
                else
                {
                    _logger.LogWarning("No replication processes found (walsender)");
                    await CheckWalSendersAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查复制进程时出错");
            }
        }

        private async Task CheckWalSendersAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                // 检查 WAL sender 配置
                await using var cmd = new NpgsqlCommand(
                    @"SELECT name, setting FROM pg_settings
                      WHERE name IN ('max_wal_senders', 'wal_level', 'archive_mode')
                      ORDER BY name",
                    connection);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                var settings = new Dictionary<string, string>();

                while (await reader.ReadAsync(cancellationToken))
                {
                    settings[reader.GetString(0)] = reader.GetString(1);
                }

                _logger.LogInformation("PostgreSQL复制相关设置: {@Settings}", settings);

                // 检查关键配置
                if (settings.TryGetValue("wal_level", out var walLevel))
                {
                    if (walLevel != "logical")
                    {
                        _logger.LogWarning("wal_level 设置为 {WalLevel}，应该设置为 'logical' 以支持逻辑复制", walLevel);
                    }
                }

                if (settings.TryGetValue("max_wal_senders", out var maxWalSenders))
                {
                    if (int.TryParse(maxWalSenders, out var maxSenders) && maxSenders == 0)
                    {
                        _logger.LogError("max_wal_senders 设置为 0，需要设置为大于 0 的值以支持复制");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查WAL sender配置时出错");
            }
        }

        private long EstimateReplicationLagMs(long lagBytes)
        {
            // 简化的延迟估算，基于字节数
            // 实际生产环境中可能需要更复杂的计算
            const long bytesPerSecond = 1024 * 1024; // 假设1MB/s
            return (lagBytes * 1000) / bytesPerSecond;
        }

        public async Task<ReplicationHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
        {
            if (SlotStatus == null)
            {
                await UpdateSlotStatusAsync(_options.Value.SlotName, cancellationToken);
            }

            var status = new ReplicationHealthStatus
            {
                IsHealthy = IsHealthy,
                SlotStatus = SlotStatus,
                ReplicationLagMs = ReplicationLagMs,
                LastChecked = LastUpdated,
                Metrics = new Dictionary<string, object>
                {
                    ["SlotName"] = _options.Value.SlotName,
                    ["PublicationName"] = _options.Value.PublicationName,
                    ["HealthCheckInterval"] = _options.Value.HealthCheckInterval,
                    ["LagThreshold"] = _options.Value.ReplicationLagThreshold
                }
            };

            if (!IsHealthy)
            {
                if (!SlotStatus?.IsActive ?? true)
                    status.Issues.Add("Replication slot is inactive");

                if (ReplicationLagMs > _options.Value.ReplicationLagThreshold)
                    status.Issues.Add($"High replication lag: {ReplicationLagMs}ms");

                if (SlotStatus?.RestartLsn == "N/A")
                    status.Issues.Add("Replication slot LSN status is abnormal");
            }

            return status;
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _semaphore?.Dispose();
        }
    }
}