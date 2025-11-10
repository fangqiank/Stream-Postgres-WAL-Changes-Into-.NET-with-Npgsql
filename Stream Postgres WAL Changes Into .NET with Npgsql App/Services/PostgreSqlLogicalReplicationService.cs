using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;

/// <summary>
/// PostgreSQL逻辑复制服务 - 使用标准的SQL命令管理发布和订阅
/// 这种方法利用PostgreSQL内置的逻辑复制机制，而不是自定义轮询
/// </summary>
public sealed class PostgreSqlLogicalReplicationService : BackgroundService
{
    private readonly ILogger<PostgreSqlLogicalReplicationService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LogicalReplicationServiceOptions _options;
    private readonly IConfiguration _configuration;

    // 状态管理
    private volatile bool _isRunning;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private LogicalReplicationServiceStatus _status = new();

    // 性能计数器
    private long _messagesReplicated;
    private long _errorCount;
    private string? _lastError;

    // 监控定时器
    private Timer? _monitoringTimer;

    public PostgreSqlLogicalReplicationService(
        ILogger<PostgreSqlLogicalReplicationService> logger,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<LogicalReplicationServiceOptions> options,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options?.CurrentValue ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 获取逻辑复制服务状态
    /// </summary>
    public LogicalReplicationServiceStatus GetStatus()
    {
        return new LogicalReplicationServiceStatus
        {
            IsRunning = _isRunning,
            StartTime = _startTime,
            Uptime = DateTime.UtcNow - _startTime,
            SubscriptionStatus = GetSubscriptionStatus(),
            LastError = _lastError,
            LastActivity = DateTime.UtcNow,
            ReplicationLagBytes = GetReplicationLag(),
            ReplicationSlotInfo = _options.ReplicationSlotName,
            MessagesReplicated = Interlocked.Read(ref _messagesReplicated),
            ErrorCount = Interlocked.Read(ref _errorCount)
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 PostgreSQL逻辑复制服务启动中...");

        try
        {
            // 等待应用程序完全启动
            await Task.Delay(_options.StartupDelay, stoppingToken);

            // 初始化连接字符串
            InitializeConnectionStrings();

            // 设置逻辑复制基础设施
            await SetupLogicalReplicationInfrastructureAsync(stoppingToken);

            // 启动监控定时器
            StartMonitoring(stoppingToken);

            _isRunning = true;
            _logger.LogInformation("✅ PostgreSQL逻辑复制服务已启动，监控复制状态...");

            // 保持服务运行，监控复制状态
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, stoppingToken);

                // 心跳日志
                _logger.LogDebug("💓 PostgreSQL逻辑复制服务心跳");
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _lastError = ex.Message;
            _logger.LogError(ex, "❌ PostgreSQL逻辑复制服务启动失败");
            throw;
        }
        finally
        {
            _isRunning = false;
            _monitoringTimer?.Dispose();
            _logger.LogInformation("🛑 PostgreSQL逻辑复制服务已停止");
        }
    }

    /// <summary>
    /// 初始化连接字符串
    /// </summary>
    private void InitializeConnectionStrings()
    {
        var sourceConnection = _configuration.GetConnectionString("DefaultConnection");
        var targetConnection = _configuration.GetConnectionString("LocalConnection");

        if (string.IsNullOrEmpty(sourceConnection))
        {
            throw new InvalidOperationException("DefaultConnection 配置缺失");
        }

        if (string.IsNullOrEmpty(targetConnection))
        {
            throw new InvalidOperationException("LocalConnection 配置缺失");
        }

        _options.SourceConnectionString = sourceConnection;
        _options.TargetConnectionString = targetConnection;

        _logger.LogInformation("📡 连接字符串已配置: Source={SourceDb}, Target={TargetDb}",
            GetDatabaseName(sourceConnection), GetDatabaseName(targetConnection));
    }

    /// <summary>
    /// 设置逻辑复制基础设施
    /// </summary>
    private async Task SetupLogicalReplicationInfrastructureAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoCreatePublicationAndSubscription)
        {
            _logger.LogInformation("⏭️ 跳过自动创建发布和订阅");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var sourceContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // 清理旧的表结构以确保新的PascalCase表能被创建
        _logger.LogInformation("🔧 开始清理旧表结构...");
        await CleanupOldTablesAsync(targetContext, cancellationToken);

        // 手动创建新的PascalCase表
        _logger.LogInformation("🔨 开始手动创建新表结构...");
        try
        {
            await CreateNewTablesManuallyAsync(targetContext, cancellationToken);
            _logger.LogInformation("✅ 手动创建新表结构完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 手动创建新表结构失败");
            throw;
        }

        // 验证新表是否创建成功
        _logger.LogInformation("🔍 验证新表创建状态...");
        await VerifyNewTablesExistAsync(targetContext, cancellationToken);

        // 在源数据库创建发布
        await CreatePublicationAsync(sourceContext, cancellationToken);

        // 在目标数据库创建订阅
        await CreateSubscriptionAsync(targetContext, cancellationToken);

        _logger.LogInformation("✅ 逻辑复制基础设施设置完成");
    }

    /// <summary>
    /// 清理旧的表结构以确保新的PascalCase表能被创建
    /// </summary>
    private async Task CleanupOldTablesAsync(LocalDbContext targetContext, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            _logger.LogInformation("🧹 开始清理旧的表结构...");

            // 删除旧的小写表名（如果存在）
            var oldTables = new[] { "orders", "outbox_events" };

            foreach (var tableName in oldTables)
            {
                await using var dropCmd = new NpgsqlCommand($"DROP TABLE IF EXISTS \"{tableName}\" CASCADE;", connection);
                await dropCmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("✅ 删除旧表: {TableName}", tableName);
            }

            _logger.LogInformation("✅ 旧表结构清理完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 清理旧表结构失败");
            throw;
        }
    }

    /// <summary>
    /// 手动创建新的PascalCase表
    /// </summary>
    private async Task CreateNewTablesManuallyAsync(LocalDbContext targetContext, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            _logger.LogInformation("🔨 开始手动创建新的PascalCase表...");

            // 创建Orders表
            await using var createOrdersCmd = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS ""Orders"" (
                    ""Id"" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""CustomerName"" VARCHAR(100) NOT NULL,
                    ""Amount"" DECIMAL(18,2) NOT NULL,
                    ""Status"" VARCHAR(50) NOT NULL DEFAULT 'Pending',
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" TIMESTAMP WITH TIME ZONE
                );", connection);
            await createOrdersCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("✅ 创建表: Orders");

            // 创建Orders表的索引
            await using var createOrdersIndex1 = new NpgsqlCommand(@"
                CREATE INDEX IF NOT EXISTS ""idx_orders_created_at"" ON ""Orders"" (""CreatedAt"");", connection);
            await createOrdersIndex1.ExecuteNonQueryAsync(cancellationToken);

            await using var createOrdersIndex2 = new NpgsqlCommand(@"
                CREATE INDEX IF NOT EXISTS ""idx_orders_status"" ON ""Orders"" (""Status"");", connection);
            await createOrdersIndex2.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("✅ 创建Orders表索引");

            // 创建OutboxEvents表
            await using var createOutboxEventsCmd = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS ""OutboxEvents"" (
                    ""Id"" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""AggregateType"" VARCHAR(100) NOT NULL,
                    ""AggregateId"" VARCHAR(50) NOT NULL,
                    ""EventType"" VARCHAR(100) NOT NULL,
                    ""Payload"" TEXT NOT NULL,
                    ""Processed"" BOOLEAN NOT NULL DEFAULT FALSE,
                    ""ProcessedAt"" TIMESTAMP WITH TIME ZONE,
                    ""CreatedAt"" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                );", connection);
            await createOutboxEventsCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("✅ 创建表: OutboxEvents");

            // 创建OutboxEvents表的索引
            await using var createOutboxIndex1 = new NpgsqlCommand(@"
                CREATE INDEX IF NOT EXISTS ""idx_outbox_events_processed_created_at"" ON ""OutboxEvents"" (""Processed"", ""CreatedAt"");", connection);
            await createOutboxIndex1.ExecuteNonQueryAsync(cancellationToken);

            await using var createOutboxIndex2 = new NpgsqlCommand(@"
                CREATE INDEX IF NOT EXISTS ""idx_outbox_events_created_at"" ON ""OutboxEvents"" (""CreatedAt"");", connection);
            await createOutboxIndex2.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("✅ 创建OutboxEvents表索引");

            _logger.LogInformation("✅ 手动创建新表完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 手动创建新表失败");
            throw;
        }
    }

    /// <summary>
    /// 验证新表是否创建成功
    /// </summary>
    private async Task VerifyNewTablesExistAsync(LocalDbContext targetContext, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            _logger.LogInformation("🔍 验证新表是否创建成功...");

            var expectedTables = new[] { "Orders", "OutboxEvents" };

            foreach (var tableName in expectedTables)
            {
                await using var checkCmd = new NpgsqlCommand(
                    "SELECT 1 FROM information_schema.tables WHERE table_name = @tableName AND table_schema = 'public'",
                    connection);
                checkCmd.Parameters.AddWithValue("@tableName", tableName);

                var result = await checkCmd.ExecuteScalarAsync(cancellationToken);
                if (result != null)
                {
                    _logger.LogInformation("✅ 表存在: {TableName}", tableName);
                }
                else
                {
                    _logger.LogWarning("⚠️ 表不存在: {TableName}", tableName);
                }
            }

            _logger.LogInformation("✅ 新表验证完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 验证新表失败");
            throw;
        }
    }

    /// <summary>
    /// 在源数据库创建发布
    /// </summary>
    private async Task CreatePublicationAsync(AppDbContext sourceContext, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            // 检查是否已存在发布
            await using var checkCmd = new NpgsqlCommand(
                @"SELECT 1 FROM pg_publication WHERE pubname = @publicationName",
                connection);
            checkCmd.Parameters.AddWithValue("@publicationName", _options.PublicationName);

            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);

            if (exists == null)
            {
                // 创建发布
                var tablesList = string.Join(", ", _options.TablesToReplicate.Select(t => $"\"{t}\""));

                await using var createCmd = new NpgsqlCommand(
                    $"CREATE PUBLICATION {_options.PublicationName} FOR TABLE {tablesList}",
                    connection);
                await createCmd.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("✅ 创建发布成功: {PublicationName}, Tables: {Tables}",
                    _options.PublicationName, tablesList);
            }
            else
            {
                _logger.LogInformation("📋 发布已存在: {PublicationName}", _options.PublicationName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 创建发布失败: {PublicationName}", _options.PublicationName);
            throw;
        }
    }

    /// <summary>
    /// 在目标数据库创建订阅
    /// </summary>
    private async Task CreateSubscriptionAsync(LocalDbContext targetContext, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync(cancellationToken);

            // 检查是否已存在订阅
            await using var checkCmd = new NpgsqlCommand(
                @"SELECT 1 FROM pg_subscription WHERE subname = @subscriptionName",
                connection);
            checkCmd.Parameters.AddWithValue("@subscriptionName", _options.SubscriptionName);

            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);

            if (exists == null)
            {
                // 解析源连接字符串获取连接信息
                var sourceBuilder = new NpgsqlConnectionStringBuilder(_options.SourceConnectionString);
                var connectionString = $"host={sourceBuilder.Host} port={sourceBuilder.Port} dbname={sourceBuilder.Database} user={sourceBuilder.Username} password={sourceBuilder.Password}";

                // 创建订阅
                await using var createCmd = new NpgsqlCommand(
                    $"CREATE SUBSCRIPTION {_options.SubscriptionName} CONNECTION '{connectionString}' PUBLICATION {_options.PublicationName} WITH (copy_data = {_options.CopyExistingDataOnStart.ToString().ToLower()})",
                    connection);
                await createCmd.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("✅ 创建订阅成功: {SubscriptionName}, Publication: {PublicationName}, CopyData: {CopyData}",
                    _options.SubscriptionName, _options.PublicationName, _options.CopyExistingDataOnStart);
            }
            else
            {
                _logger.LogInformation("📋 订阅已存在: {SubscriptionName}", _options.SubscriptionName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 创建订阅失败: {SubscriptionName}", _options.SubscriptionName);
            throw;
        }
    }

    /// <summary>
    /// 启动监控
    /// </summary>
    private void StartMonitoring(CancellationToken cancellationToken)
    {
        _monitoringTimer = new Timer(async _ =>
        {
            try
            {
                await MonitorReplicationStatusAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errorCount);
                _lastError = ex.Message;
                _logger.LogError(ex, "❌ 监控复制状态失败");
            }
        }, null, TimeSpan.Zero, _options.HeartbeatInterval);
    }

    /// <summary>
    /// 监控复制状态
    /// </summary>
    private async Task MonitorReplicationStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = GetReplicationLag();
            var subscriptionStatus = GetSubscriptionStatus();

            if (status.HasValue && status.Value > 1024 * 1024) // 1MB延迟阈值
            {
                _logger.LogWarning("⚠️ 复制延迟较高: {LagBytes} bytes", status.Value);
            }

            _logger.LogDebug("📊 复制状态: {Status}, 延迟: {LagBytes} bytes", subscriptionStatus, status);

            Interlocked.Increment(ref _messagesReplicated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 监控复制状态时发生错误");
        }
    }

    /// <summary>
    /// 获取订阅状态
    /// </summary>
    private string GetSubscriptionStatus()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            connection.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT subenabled, subslotname, subconninfo, subpublications
                  FROM pg_subscription WHERE subname = @subscriptionName",
                connection);
            cmd.Parameters.AddWithValue("@subscriptionName", _options.SubscriptionName);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var enabled = reader.GetBoolean(0);
                var slotName = reader.GetString(1);
                var publications = reader.GetValue(3); // Use GetValue for text[] array
                var publicationsStr = publications is string[] pubArray ? string.Join(", ", pubArray) : publications.ToString();
                return enabled ? $"Active (Slot: {slotName}, Pubs: {publicationsStr})" : "Inactive";
            }

            return "Not Found";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 获取订阅状态失败");
            return "Error";
        }
    }

    /// <summary>
    /// 获取复制延迟
    /// </summary>
    private long? GetReplicationLag()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            connection.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT pg_wal_lsn_diff(pg_current_wal_lsn(), replay_lsn)
                  FROM pg_stat_replication WHERE application_name = @subscriptionName",
                connection);
            cmd.Parameters.AddWithValue("@subscriptionName", _options.SubscriptionName);

            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt64(result) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 获取复制延迟失败");
            return null;
        }
    }

    /// <summary>
    /// 从连接字符串获取数据库名称
    /// </summary>
    private string GetDatabaseName(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return builder.Database ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// 停止复制
    /// </summary>
    public override void Dispose()
    {
        try
        {
            _isRunning = false;
            _monitoringTimer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 停止复制服务时发生错误");
        }
        finally
        {
            base.Dispose();
        }
    }
}