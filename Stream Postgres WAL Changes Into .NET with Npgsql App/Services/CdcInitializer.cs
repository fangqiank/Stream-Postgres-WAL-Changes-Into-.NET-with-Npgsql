using Microsoft.Extensions.Options;
using Npgsql;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// CDC初始化服务
    /// </summary>
    public class CdcInitializer : BackgroundService
    {
        private readonly ILogger<CdcInitializer> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptions<CdcOptions> _cdcOptions;
        private readonly IOptions<LogicalReplicationOptions> _replicationOptions;

        public CdcInitializer(
            ILogger<CdcInitializer> logger,
            IServiceProvider serviceProvider,
            IOptions<CdcOptions> cdcOptions,
            IOptions<LogicalReplicationOptions> replicationOptions)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _cdcOptions = cdcOptions;
            _replicationOptions = replicationOptions;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_cdcOptions.Value.Enabled)
            {
                _logger.LogInformation("CDC is disabled in configuration");
                return;
            }

            _logger.LogInformation("Starting CDC initialization, delay: {Delay}s", _cdcOptions.Value.StartupDelay);
            await Task.Delay(TimeSpan.FromSeconds(_cdcOptions.Value.StartupDelay), stoppingToken);

            try
            {
                await InitializeCdcAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CDC initialization failed");
                throw;
            }
        }

        private async Task InitializeCdcAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing CDC system...");

            // 1. 确保数据库表存在
            await EnsureDatabaseSchemaAsync(cancellationToken);

            // 2. 确保CDC配置正确
            await ValidateCdcConfigurationAsync(cancellationToken);

            // 3. 创建死信队列表（如果启用）
            if (_cdcOptions.Value.EnableDeadLetterQueue)
            {
                await CreateDeadLetterTableAsync(cancellationToken);
            }

            // 4. 创建事件序列化目录（如果启用）
            if (_cdcOptions.Value.EnableEventSerialization)
            {
                CreateEventSerializationDirectory();
            }

            // 5. 订阅表变更
            await SubscribeToTablesAsync(cancellationToken);

            // 6. 启动CDC服务
            var cdcService = _serviceProvider.GetRequiredService<ICdcService>();
            await cdcService.StartListeningAsync(cancellationToken);

            _logger.LogInformation("CDC initialization completed successfully");
        }

        private async Task EnsureDatabaseSchemaAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                await dbContext.Database.EnsureCreatedAsync(cancellationToken);
                _logger.LogInformation("Database schema ensured");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure database schema");
                throw;
            }
        }

        private async Task ValidateCdcConfigurationAsync(CancellationToken cancellationToken)
        {
            var cdcService = _serviceProvider.GetRequiredService<ICdcService>();

            // 检查复制槽状态
            var status = cdcService.GetStatus();
            if (!status.IsActive)
            {
                _logger.LogInformation("CDC service is not active, this is normal during startup");
            }

            // 验证配置的表
            foreach (var table in _replicationOptions.Value.ReplicatedTables)
            {
                await ValidateTableExistsAsync(table, cancellationToken);
            }

            _logger.LogInformation("CDC configuration validation completed");
        }

        private async Task ValidateTableExistsAsync(string tableName, CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                // 检查表是否存在 - 尝试多种可能的表名变体
                var connection = new NpgsqlConnection(_serviceProvider.GetRequiredService<IConfiguration>()
                    .GetConnectionString("DefaultConnection"));
                await connection.OpenAsync(cancellationToken);

                var tableNameVariants = new[]
                {
                    tableName,                    // 原始名称
                    tableName.ToLowerInvariant(), // 小写版本
                    tableName.ToUpperInvariant(), // 大写版本
                    // 根据Entity Framework配置，可能的表名格式
                    char.ToUpper(tableName[0]) + tableName.Substring(1).ToLowerInvariant() // 首字母大写
                };

                bool tableExists = false;
                string foundTableName = string.Empty;

                foreach (var variant in tableNameVariants)
                {
                    await using var cmd = new NpgsqlCommand(
                        "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = @tableName)",
                        connection);
                    cmd.Parameters.AddWithValue("tableName", variant);

                    var exists = (bool?)await cmd.ExecuteScalarAsync(cancellationToken);
                    if (exists == true)
                    {
                        tableExists = true;
                        foundTableName = variant;
                        _logger.LogDebug("Table '{OriginalName}' exists as '{FoundTableName}'", tableName, foundTableName);
                        break;
                    }
                }

                if (!tableExists)
                {
                    _logger.LogWarning("Table {TableName} does not exist (checked multiple variants), CDC may not capture changes from this table", tableName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate table existence for: {TableName}", tableName);
            }
        }

        private async Task CreateDeadLetterTableAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            try
            {
                var connection = new NpgsqlConnection(_serviceProvider.GetRequiredService<IConfiguration>()
                    .GetConnectionString("DefaultConnection"));
                await connection.OpenAsync(cancellationToken);

                var createTableSql = $@"
                    CREATE TABLE IF NOT EXISTS {_cdcOptions.Value.DeadLetterQueueTable} (
                        id BIGSERIAL PRIMARY KEY,
                        table_name VARCHAR(255) NOT NULL,
                        event_type VARCHAR(50) NOT NULL,
                        event_data JSONB,
                        error_message TEXT,
                        error_stack_trace TEXT,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                        retry_count INT DEFAULT 0,
                        original_lsn VARCHAR(100)
                    );
                ";

                await using var cmd = new NpgsqlCommand(createTableSql, connection);
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("Dead letter table created: {TableName}", _cdcOptions.Value.DeadLetterQueueTable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create dead letter table");
                throw;
            }
        }

        private void CreateEventSerializationDirectory()
        {
            try
            {
                var directory = _cdcOptions.Value.EventSerializationDirectory;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("Created event serialization directory: {Directory}", directory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create event serialization directory");
            }
        }

        private async Task SubscribeToTablesAsync(CancellationToken cancellationToken)
        {
            var cdcService = _serviceProvider.GetRequiredService<ICdcService>();
            var eventHandlerManager = _serviceProvider.GetRequiredService<CdcEventHandlerManager>();

            // 为每个表订阅变更事件
            foreach (var table in _replicationOptions.Value.ReplicatedTables)
            {
                await cdcService.SubscribeAsync(table, async (changeEvent) =>
                {
                    await eventHandlerManager.HandleEventAsync(changeEvent, cancellationToken);
                }, cancellationToken);

                _logger.LogInformation("Subscribed to table: {TableName}", table);
            }
        }
    }
}