using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using System.Data;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// Improved logical replication service
    /// Note: This is a simplified implementation, true logical replication requires more complex setup
    /// </summary>
    public class LogicalReplicationService : BackgroundService
    {
        private readonly ILogger<LogicalReplicationService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptions<LogicalReplicationOptions> _options;
        private readonly IReplicationHealthMonitor _healthMonitor;
        private readonly IReplicationEventProcessor _eventProcessor;

        private NpgsqlConnection? _monitoringConnection;
        private int _consecutiveErrors = 0;
        private readonly Timer _replicationCheckTimer;

        public LogicalReplicationService(
            ILogger<LogicalReplicationService> logger,
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            IOptions<LogicalReplicationOptions> options,
            IReplicationHealthMonitor healthMonitor,
            IReplicationEventProcessor eventProcessor)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _options = options;
            _healthMonitor = healthMonitor;
            _eventProcessor = eventProcessor;

            // 设置定时检查复制状态的定时器
            _replicationCheckTimer = new Timer(
                async _ => await CheckReplicationStatusAsync(),
                null,
                TimeSpan.FromMinutes(1), // 1分钟后开始
                TimeSpan.FromMinutes(_options.Value.HealthCheckInterval));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting logical replication service...");

            // 等待应用完全启动
            await Task.Delay(TimeSpan.FromSeconds(_options.Value.HeartbeatInterval), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EnsureReplicationSetupAsync(stoppingToken);

                    // 在这个简化版本中，我们主要监控复制状态
                    // 真正的逻辑复制需要PostgreSQL的特定配置和权限
                    await MonitorReplicationStatusAsync(stoppingToken);

                    // 成功执行，重置错误计数
                    _consecutiveErrors = 0;

                    // 短暂等待后继续监控
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("逻辑复制服务正在停止...");
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveErrors++;
                    _logger.LogError(ex, "逻辑复制服务发生错误 (第 {ErrorCount} 次连续错误)", _consecutiveErrors);

                    // 检查是否超过最大重试次数
                    if (_consecutiveErrors >= _options.Value.MaxRetryAttempts)
                    {
                        _logger.LogError("超过最大重试次数 ({MaxAttempts})，服务将停止", _options.Value.MaxRetryAttempts);
                        break;
                    }

                    // 计算重试延迟（指数退避）
                    var retryDelay = CalculateRetryDelay(_consecutiveErrors);
                    _logger.LogInformation("将在 {Delay} 秒后重试", retryDelay.TotalSeconds);

                    try
                    {
                        await Task.Delay(retryDelay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("重试期间服务停止");
                        break;
                    }
                }
            }

            _logger.LogInformation("逻辑复制服务已停止");
        }

        private async Task EnsureReplicationSetupAsync(CancellationToken cancellationToken)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("数据库连接字符串未配置");

            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // 确保复制用户权限
            await EnsureReplicationPermissionsAsync(connection, cancellationToken);

            // 检查并创建发布
            await EnsurePublicationExistsAsync(connection, cancellationToken);

            // 检查复制槽
            await CheckReplicationSlotAsync(connection, cancellationToken);
        }

        private async Task EnsureReplicationPermissionsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                await using var cmd = new NpgsqlCommand(
                    @"SELECT 1 FROM pg_roles WHERE rolreplication = true AND rolname = current_user",
                    connection);

                var hasReplicationRole = await cmd.ExecuteScalarAsync(cancellationToken) != null;

                if (!hasReplicationRole)
                {
                    _logger.LogWarning("当前用户没有复制权限，逻辑复制可能无法正常工作");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "检查复制权限时出错");
            }
        }

        private async Task EnsurePublicationExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                await using var cmdCheckPub = new NpgsqlCommand(
                    "SELECT pubname FROM pg_publication WHERE pubname = $1",
                    connection);
                cmdCheckPub.Parameters.AddWithValue(_options.Value.PublicationName);

                var existingPub = await cmdCheckPub.ExecuteScalarAsync(cancellationToken);
                if (existingPub == null)
                {
                    await using var cmdCreatePub = new NpgsqlCommand(
                        $"CREATE PUBLICATION {_options.Value.PublicationName} FOR TABLE {string.Join(", ", _options.Value.ReplicatedTables.Select(t => $"\"{t}\""))}",
                        connection);
                    await cmdCreatePub.ExecuteNonQueryAsync(cancellationToken);

                    _logger.LogInformation("创建发布: {PublicationName}，包含表: {Tables}",
                        _options.Value.PublicationName,
                        string.Join(", ", _options.Value.ReplicatedTables));
                }
                else
                {
                    _logger.LogDebug("发布已存在: {PublicationName}", _options.Value.PublicationName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建发布失败");
                throw;
            }
        }

        private async Task CheckReplicationSlotAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                await using var cmdCheckSlot = new NpgsqlCommand(
                    @"SELECT slot_name, active, active_pid, restart_lsn, confirmed_flush_lsn
                      FROM pg_replication_slots WHERE slot_name = $1",
                    connection);
                cmdCheckSlot.Parameters.AddWithValue(_options.Value.SlotName);

                await using var reader = await cmdCheckSlot.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var slotName = reader.GetString(0);
                    var isActive = reader.GetBoolean(1);
                    var activePid = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2);
                    var restartLsn = reader.IsDBNull(3) ? "N/A" : reader.GetValue(3)?.ToString();
                    var confirmedLsn = reader.IsDBNull(4) ? "N/A" : reader.GetValue(4)?.ToString() ?? "N/A";

                    _logger.LogInformation("Replication slot status - Name: {SlotName}, Active: {Active}, PID: {Pid}, LSN: {LSN}",
                        slotName, isActive, activePid, confirmedLsn);

                    if (!isActive)
                    {
                        _logger.LogWarning("Replication slot {SlotName} exists but is inactive, LSN: {LSN}", slotName, confirmedLsn);

                        // 检查是否有僵尸进程
                        if (activePid.HasValue)
                        {
                            _logger.LogInformation("Replication slot has process ID {Pid} but status is inactive, may need cleanup", activePid);
                        }

                        // 尝试激活复制槽或提供解决方案
                        await HandleInactiveSlotAsync(slotName, confirmedLsn, cancellationToken);
                    }
                    else
                    {
                        _logger.LogInformation("复制槽 {SlotName} 正常活跃", slotName);
                    }
                }
                else
                {
                    _logger.LogInformation("Replication slot {SlotName} does not exist, attempting to create", _options.Value.SlotName);
                    await CreateReplicationSlotAsync(connection, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查复制槽时出错");
                throw;
            }
        }

        private async Task HandleInactiveSlotAsync(string slotName, string confirmedLsn, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Handling inactive replication slot: {SlotName}", slotName);

            // 方案1: 检查是否需要重置复制槽
            if (ShouldResetSlot(confirmedLsn))
            {
                _logger.LogWarning("复制槽 {SlotName} 可能需要重置，LSN过于陈旧: {LSN}", slotName, confirmedLsn);
                await ProvideSlotResetInstructionsAsync(slotName, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Replication slot {SlotName} can continue to be used, waiting for replication connection", slotName);
            }
        }

        private bool ShouldResetSlot(string confirmedLsn)
        {
            // 简单判断：如果LSN是0或非常小，可能需要重置
            if (confirmedLsn == "N/A" || confirmedLsn == "0/0")
                return true;

            // 这里可以添加更复杂的LSN比较逻辑
            return false;
        }

        private async Task CreateReplicationSlotAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            if (!_options.Value.CreateSlotIfNotExists)
            {
                _logger.LogInformation("配置禁用了自动创建复制槽");
                return;
            }

            try
            {
                await using var cmdCreateSlot = new NpgsqlCommand(
                    $"SELECT pg_create_logical_replication_slot('{_options.Value.SlotName}', '{_options.Value.WalDecoderPlugin}')",
                    connection);
                await cmdCreateSlot.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("成功创建复制槽: {SlotName}", _options.Value.SlotName);
            }
            catch (PostgresException ex) when (ex.SqlState == "42710")
            {
                _logger.LogInformation("复制槽 {SlotName} 已存在，跳过创建", _options.Value.SlotName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建复制槽失败: {SlotName}", _options.Value.SlotName);
                throw;
            }
        }

        private async Task ProvideSlotResetInstructionsAsync(string slotName, CancellationToken cancellationToken)
        {
            _logger.LogWarning(@"Replication slot {SlotName} reset instructions:
1. Replication slot exists but is not active
2. This usually happens in the following situations:
   - Previous replication connection was unexpectedly interrupted
   - Replication connection was not restored after database restart
   - Replication permission issues

3. Solutions:
   a) Check current replication slot status:
      SELECT * FROM pg_replication_slots WHERE slot_name = '{0}';

   b) If you need to reset the replication slot, run:
      -- WARNING: This will lose unprocessed WAL records
      SELECT pg_drop_replication_slot('{0}');
      SELECT pg_create_logical_replication_slot('{0}', 'pgoutput');

   c) Or wait for a new replication connection to activate the replication slot

4. The current application will continue processing events using the Outbox pattern", slotName);
        }

        private async Task MonitorReplicationStatusAsync(CancellationToken cancellationToken)
        {
            // Simulate replication monitoring - in real implementation, this would listen to WAL changes

            // Update health monitor status
            await _healthMonitor.UpdateSlotStatusAsync(_options.Value.SlotName, cancellationToken);

            // Get current health status
            var healthStatus = await _healthMonitor.GetHealthStatusAsync(cancellationToken);

            if (!healthStatus.IsHealthy)
            {
                _logger.LogWarning("Replication health check found issues: {Issues}", string.Join(", ", healthStatus.Issues));
            }

            // 模拟处理一些事件（在实际实现中，这些会来自WAL变更）
            await SimulateEventProcessingAsync(cancellationToken);
        }

        private async Task SimulateEventProcessingAsync(CancellationToken cancellationToken)
        {
            // 这里只是演示如何处理事件
            // 在实际实现中，这些事件会来自PostgreSQL的逻辑复制流
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 检查是否有新的OutboxEvents需要处理
                var unprocessedEvents = await dbContext.OutboxEvents
                    .Where(oe => !oe.Processed)
                    .OrderBy(oe => oe.CreatedAt)
                    .Take(10)
                    .ToListAsync(cancellationToken);

                if (unprocessedEvents.Any())
                {
                    foreach (var outboxEvent in unprocessedEvents)
                    {
                        var replicationEvent = new ReplicationEvent
                        {
                            EventType = "INSERT",
                            SchemaName = "public",
                            TableName = "OutboxEvents",
                            EventTime = outboxEvent.CreatedAt,
                            TransactionId = outboxEvent.Id.ToString(),
                            Lsn = "simulated",
                            NewValues = new Dictionary<string, object>
                            {
                                ["Id"] = outboxEvent.Id,
                                ["EventType"] = outboxEvent.EventType,
                                ["AggregateType"] = outboxEvent.AggregateType,
                                ["AggregateId"] = outboxEvent.AggregateId,
                                ["CreatedAt"] = outboxEvent.CreatedAt
                            }
                        };

                        await _eventProcessor.ProcessEventAsync(replicationEvent, cancellationToken);
                    }

                    _logger.LogInformation("模拟处理了 {Count} 个事件", unprocessedEvents.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "模拟事件处理时出错");
            }
        }

        private async Task CheckReplicationStatusAsync()
        {
            try
            {
                await _healthMonitor.UpdateSlotStatusAsync(_options.Value.SlotName);
                var healthStatus = await _healthMonitor.GetHealthStatusAsync();

                _logger.LogDebug("定时复制状态检查 - 健康: {Healthy}, 延迟: {Lag}ms",
                    healthStatus.IsHealthy, healthStatus.ReplicationLagMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "定时复制状态检查失败");
            }
        }

        private TimeSpan CalculateRetryDelay(int errorCount)
        {
            // 指数退避算法，最大不超过5分钟
            var baseDelay = TimeSpan.FromSeconds(_options.Value.RetryInterval / 1000.0);
            var maxDelay = TimeSpan.FromMinutes(5);

            var delay = TimeSpan.FromSeconds(Math.Pow(2, errorCount - 1) * baseDelay.TotalSeconds);
            return delay > maxDelay ? maxDelay : delay;
        }

        private async Task CleanupConnectionAsync()
        {
            try
            {
                if (_monitoringConnection != null)
                {
                    if (_monitoringConnection.State == ConnectionState.Open)
                    {
                        await _monitoringConnection.CloseAsync();
                    }
                    _monitoringConnection.Dispose();
                    _monitoringConnection = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理连接时出错");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止逻辑复制服务...");

            _replicationCheckTimer?.Dispose();
            await CleanupConnectionAsync();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _replicationCheckTimer?.Dispose();
            _monitoringConnection?.Dispose();
            base.Dispose();
        }
    }
}