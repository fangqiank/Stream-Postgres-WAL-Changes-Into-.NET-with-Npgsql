using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Core.DataSources;
using DebeziumDemoApp.Core.DataTargets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DebeziumDemoApp.Core.Services;

/// <summary>
/// 通用数据同步服务，支持多种数据源和数据目标之间的实时数据同步
/// </summary>
public class UniversalDataSyncService : BackgroundService, IUniversalDataSyncService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UniversalDataSyncService> _logger;
    private readonly ConcurrentDictionary<string, IDataSource> _dataSources = new();
    private readonly ConcurrentDictionary<string, IDataTarget> _dataTargets = new();
    private readonly ConcurrentDictionary<string, SyncPipeline> _syncPipelines = new();
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized = false;
    private readonly DataSyncStatistics _statistics = new();

    public event EventHandler<SyncPipelineEventArgs>? OnPipelineStarted;
    public event EventHandler<SyncPipelineEventArgs>? OnPipelineStopped;
    public event EventHandler<SyncPipelineEventArgs>? OnPipelineError;
    public event EventHandler<DataSyncEventArgs>? OnDataSynced;

    public UniversalDataSyncService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<UniversalDataSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_isInitialized)
            {
                _logger.LogInformation("[UNIVERSAL_SYNC] Service already initialized");
                return;
            }

            _logger.LogInformation("[UNIVERSAL_SYNC] Initializing universal data sync service");

            // 初始化数据源
            await InitializeDataSourcesAsync(cancellationToken);

            // 初始化数据目标
            await InitializeDataTargetsAsync(cancellationToken);

            // 创建同步管道
            await CreateSyncPipelinesAsync(cancellationToken);

            // 启动所有数据源
            await StartDataSourcesAsync(cancellationToken);

            _isInitialized = true;
            _logger.LogInformation("[UNIVERSAL_SYNC] Universal data sync service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UNIVERSAL_SYNC] Failed to initialize universal data sync service");
            throw;
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    public async Task AddDataSourceAsync(string name, IDataSource dataSource, CancellationToken cancellationToken = default)
    {
        if (_dataSources.ContainsKey(name))
        {
            throw new ArgumentException($"Data source '{name}' already exists");
        }

        _dataSources[name] = dataSource;

        // 订阅数据源变更事件
        dataSource.OnChange += async (sender, args) =>
        {
            await HandleDataSourceChangeAsync(name, args.Change, cancellationToken);
        };

        // 如果已初始化，立即连接
        if (_isInitialized)
        {
            await dataSource.ConnectAsync(cancellationToken);
            _logger.LogInformation("[UNIVERSAL_SYNC] Added and connected data source: {Name}", name);
        }
    }

    public async Task AddDataTargetAsync(string name, IDataTarget dataTarget, CancellationToken cancellationToken = default)
    {
        if (_dataTargets.ContainsKey(name))
        {
            throw new ArgumentException($"Data target '{name}' already exists");
        }

        _dataTargets[name] = dataTarget;

        // 如果已初始化，立即连接
        if (_isInitialized)
        {
            await dataTarget.ConnectAsync(cancellationToken);
            _logger.LogInformation("[UNIVERSAL_SYNC] Added and connected data target: {Name}", name);
        }
    }

    public async Task CreatePipelineAsync(
        string pipelineName,
        string sourceName,
        string targetName,
        SyncPipelineConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!_dataSources.ContainsKey(sourceName))
        {
            throw new ArgumentException($"Data source '{sourceName}' not found");
        }

        if (!_dataTargets.ContainsKey(targetName))
        {
            throw new ArgumentException($"Data target '{targetName}' not found");
        }

        if (_syncPipelines.ContainsKey(pipelineName))
        {
            throw new ArgumentException($"Sync pipeline '{pipelineName}' already exists");
        }

        var pipeline = new SyncPipeline
        {
            Name = pipelineName,
            SourceName = sourceName,
            TargetName = targetName,
            Configuration = configuration,
            IsEnabled = configuration.Enabled,
            CreatedAt = DateTime.UtcNow
        };

        _syncPipelines[pipelineName] = pipeline;

        _logger.LogInformation("[UNIVERSAL_SYNC] Created sync pipeline: {PipelineName} from {Source} to {Target}",
            pipelineName, sourceName, targetName);

        await Task.CompletedTask;
    }

    public async Task EnablePipelineAsync(string pipelineName, CancellationToken cancellationToken = default)
    {
        if (!_syncPipelines.TryGetValue(pipelineName, out var pipeline))
        {
            throw new ArgumentException($"Sync pipeline '{pipelineName}' not found");
        }

        pipeline.IsEnabled = true;
        pipeline.EnabledAt = DateTime.UtcNow;

        _logger.LogInformation("[UNIVERSAL_SYNC] Enabled sync pipeline: {PipelineName}", pipelineName);
        await Task.CompletedTask;
    }

    public async Task DisablePipelineAsync(string pipelineName, CancellationToken cancellationToken = default)
    {
        if (!_syncPipelines.TryGetValue(pipelineName, out var pipeline))
        {
            throw new ArgumentException($"Sync pipeline '{pipelineName}' not found");
        }

        pipeline.IsEnabled = false;
        pipeline.DisabledAt = DateTime.UtcNow;

        _logger.LogInformation("[UNIVERSAL_SYNC] Disabled sync pipeline: {PipelineName}", pipelineName);
        await Task.CompletedTask;
    }

    public async Task<DataSyncStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await UpdateStatisticsAsync(cancellationToken);
        return _statistics;
    }

    public async Task<List<DataSourceHealth>> GetDataSourceHealthAsync(CancellationToken cancellationToken = default)
    {
        var healthList = new List<DataSourceHealth>();

        foreach (var kvp in _dataSources)
        {
            try
            {
                var health = await kvp.Value.CheckHealthAsync();
                healthList.Add(health);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UNIVERSAL_SYNC] Failed to check health for data source: {Name}", kvp.Key);
                healthList.Add(new DataSourceHealth
                {
                    IsHealthy = false,
                    Status = "Error",
                    Message = ex.Message,
                    LastCheck = DateTime.UtcNow
                });
            }
        }

        return healthList;
    }

    public async Task<List<DataTargetHealth>> GetDataTargetHealthAsync(CancellationToken cancellationToken = default)
    {
        var healthList = new List<DataTargetHealth>();

        foreach (var kvp in _dataTargets)
        {
            try
            {
                var health = await kvp.Value.CheckHealthAsync();
                healthList.Add(health);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UNIVERSAL_SYNC] Failed to check health for data target: {Name}", kvp.Key);
                healthList.Add(new DataTargetHealth
                {
                    IsHealthy = false,
                    Status = "Error",
                    Message = ex.Message,
                    LastCheck = DateTime.UtcNow
                });
            }
        }

        return healthList;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await InitializeAsync(stoppingToken);

            _logger.LogInformation("[UNIVERSAL_SYNC] Universal data sync service started");

            // 主服务循环，处理健康检查和统计更新
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformHealthCheckAsync(stoppingToken);
                    await UpdateStatisticsAsync(stoppingToken);

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[UNIVERSAL_SYNC] Error in main service loop");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UNIVERSAL_SYNC] Fatal error in universal data sync service");
        }
        finally
        {
            await CleanupAsync();
            _logger.LogInformation("[UNIVERSAL_SYNC] Universal data sync service stopped");
        }
    }

    private async Task InitializeDataSourcesAsync(CancellationToken cancellationToken)
    {
        var dataSourcesSection = _configuration.GetSection("DataSources");

        foreach (var sourceSection in dataSourcesSection.GetChildren())
        {
            var name = sourceSection.Key;
            var type = sourceSection.GetValue<string>("Type");

            IDataSource? dataSource = type switch
            {
                "RabbitMQ" => ActivatorUtilities.CreateInstance<RabbitMQDataSource>(_serviceProvider, name),
                "PostgreSQL" => ActivatorUtilities.CreateInstance<PostgreSQLDataSource>(_serviceProvider, name),
                "SQLServer" => ActivatorUtilities.CreateInstance<SQLServerDataSource>(_serviceProvider, name),
                "MongoDB" => ActivatorUtilities.CreateInstance<MongoDBDataSource>(_serviceProvider, name),
                _ => throw new ArgumentException($"Unsupported data source type: {type}")
            };

            await AddDataSourceAsync(name, dataSource, cancellationToken);
        }
    }

    private async Task InitializeDataTargetsAsync(CancellationToken cancellationToken)
    {
        var dataTargetsSection = _configuration.GetSection("DataTargets");

        foreach (var targetSection in dataTargetsSection.GetChildren())
        {
            var name = targetSection.Key;
            var type = targetSection.GetValue<string>("Type");

            IDataTarget? dataTarget = type switch
            {
                "PostgreSQL" => ActivatorUtilities.CreateInstance<PostgreSQLDataTarget>(_serviceProvider, name),
                "MongoDB" => ActivatorUtilities.CreateInstance<MongoDBDataTarget>(_serviceProvider, name),
                "SQLServer" => ActivatorUtilities.CreateInstance<SQLServerDataTarget>(_serviceProvider, name),
                _ => throw new ArgumentException($"Unsupported data target type: {type}")
            };

            await AddDataTargetAsync(name, dataTarget, cancellationToken);
        }
    }

    private async Task CreateSyncPipelinesAsync(CancellationToken cancellationToken)
    {
        var pipelinesSection = _configuration.GetSection("SyncPipelines");

        foreach (var pipelineSection in pipelinesSection.GetChildren())
        {
            var name = pipelineSection.Key;
            var sourceName = pipelineSection.GetValue<string>("Source");
            var targetName = pipelineSection.GetValue<string>("Target");
            var configuration = new SyncPipelineConfiguration
            {
                Enabled = pipelineSection.GetValue<bool>("Enabled", true),
                BatchSize = pipelineSection.GetValue<int>("BatchSize", 100),
                FilterExpression = pipelineSection.GetValue<string>("FilterExpression"),
                RetryPolicy = pipelineSection.GetValue<string>("RetryPolicy", "Exponential"),
                MaxRetries = pipelineSection.GetValue<int>("MaxRetries", 3)
            };

            await CreatePipelineAsync(name, sourceName, targetName, configuration, cancellationToken);
        }
    }

    private async Task StartDataSourcesAsync(CancellationToken cancellationToken)
    {
        foreach (var kvp in _dataSources)
        {
            try
            {
                await kvp.Value.ConnectAsync(cancellationToken);
                _logger.LogInformation("[UNIVERSAL_SYNC] Started data source: {Name}", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UNIVERSAL_SYNC] Failed to start data source: {Name}", kvp.Key);
            }
        }
    }

    private async Task HandleDataSourceChangeAsync(
        string sourceName,
        DatabaseChange change,
        CancellationToken cancellationToken)
    {
        try
        {
            _statistics.TotalChangesReceived++;

            // 查找相关的同步管道
            var relevantPipelines = _syncPipelines.Values
                .Where(p => p.IsEnabled && p.SourceName == sourceName)
                .Where(p => ShouldProcessChange(p, change))
                .ToList();

            foreach (var pipeline in relevantPipelines)
            {
                await ProcessChangeThroughPipelineAsync(pipeline, change, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UNIVERSAL_SYNC] Error handling change from data source: {Source}", sourceName);
            _statistics.TotalErrors++;
        }
    }

    private async Task ProcessChangeThroughPipelineAsync(
        SyncPipeline pipeline,
        DatabaseChange change,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!_dataTargets.TryGetValue(pipeline.TargetName, out var target))
            {
                _logger.LogWarning("[UNIVERSAL_SYNC] Data target not found for pipeline: {Pipeline}", pipeline.Name);
                return;
            }

            var result = await target.WriteChangeAsync(change, cancellationToken);

            if (result.Success)
            {
                _statistics.TotalChangesProcessed++;
                pipeline.Statistics.SuccessfulChanges++;
                _logger.LogDebug("[UNIVERSAL_SYNC] Successfully processed change through pipeline: {Pipeline}", pipeline.Name);
            }
            else
            {
                _statistics.TotalErrors++;
                pipeline.Statistics.FailedChanges++;
                _logger.LogWarning("[UNIVERSAL_SYNC] Failed to process change through pipeline: {Pipeline}, Error: {Error}",
                    pipeline.Name, result.ErrorMessage);
            }

            // 触发数据同步事件
            OnDataSynced?.Invoke(this, new DataSyncEventArgs
            {
                PipelineName = pipeline.Name,
                Change = change,
                Result = result,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            _statistics.TotalErrors++;
            pipeline.Statistics.FailedChanges++;

            _logger.LogError(ex, "[UNIVERSAL_SYNC] Error processing change through pipeline: {Pipeline}", pipeline.Name);
            OnPipelineError?.Invoke(this, new SyncPipelineEventArgs
            {
                PipelineName = pipeline.Name,
                Message = ex.Message,
                Exception = ex
            });
        }
        finally
        {
            stopwatch.Stop();
            pipeline.Statistics.AverageProcessingTimeMs =
                (pipeline.Statistics.AverageProcessingTimeMs * (pipeline.Statistics.SuccessfulChanges + pipeline.Statistics.FailedChanges - 1) + stopwatch.ElapsedMilliseconds) /
                (pipeline.Statistics.SuccessfulChanges + pipeline.Statistics.FailedChanges);
        }
    }

    private bool ShouldProcessChange(SyncPipeline pipeline, DatabaseChange change)
    {
        // 检查过滤器表达式
        if (!string.IsNullOrEmpty(pipeline.Configuration.FilterExpression))
        {
            try
            {
                // 简单的过滤器实现，可以扩展为更复杂的表达式引擎
                var filter = pipeline.Configuration.FilterExpression;
                if (filter.Contains("table:") && !filter.Contains(change.Table))
                {
                    return false;
                }

                if (filter.Contains("operation:") && !filter.Contains(change.Operation))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UNIVERSAL_SYNC] Error applying filter for pipeline: {Pipeline}", pipeline.Name);
                return false;
            }
        }

        return true;
    }

    private async Task PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sourceHealth = await GetDataSourceHealthAsync(cancellationToken);
            var targetHealth = await GetDataTargetHealthAsync(cancellationToken);

            var allHealthy = sourceHealth.All(h => h.IsHealthy) && targetHealth.All(h => h.IsHealthy);

            if (!allHealthy)
            {
                _logger.LogWarning("[UNIVERSAL_SYNC] Some data sources or targets are unhealthy");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UNIVERSAL_SYNC] Error during health check");
        }
    }

    private async Task UpdateStatisticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _statistics.ActivePipelines = _syncPipelines.Count(p => p.Value.IsEnabled);
            _statistics.TotalPipelines = _syncPipelines.Count;
            _statistics.ActiveDataSources = _dataSources.Count(kvp => kvp.Value.IsConnected);
            _statistics.ActiveDataTargets = _dataTargets.Count(kvp => kvp.Value.IsConnected);
            _statistics.LastUpdated = DateTime.UtcNow;

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UNIVERSAL_SYNC] Error updating statistics");
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            // 断开所有数据源连接
            foreach (var dataSource in _dataSources.Values)
            {
                try
                {
                    await dataSource.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[UNIVERSAL_SYNC] Error disconnecting data source");
                }
            }

            // 断开所有数据目标连接
            foreach (var dataTarget in _dataTargets.Values)
            {
                try
                {
                    await dataTarget.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[UNIVERSAL_SYNC] Error disconnecting data target");
                }
            }

            _dataSources.Clear();
            _dataTargets.Clear();
            _syncPipelines.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UNIVERSAL_SYNC] Error during cleanup");
        }
    }
}

/// <summary>
/// 通用数据同步服务接口
/// </summary>
public interface IUniversalDataSyncService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddDataSourceAsync(string name, IDataSource dataSource, CancellationToken cancellationToken = default);
    Task AddDataTargetAsync(string name, IDataTarget dataTarget, CancellationToken cancellationToken = default);
    Task CreatePipelineAsync(string pipelineName, string sourceName, string targetName, SyncPipelineConfiguration configuration, CancellationToken cancellationToken = default);
    Task EnablePipelineAsync(string pipelineName, CancellationToken cancellationToken = default);
    Task DisablePipelineAsync(string pipelineName, CancellationToken cancellationToken = default);
    Task<DataSyncStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<List<DataSourceHealth>> GetDataSourceHealthAsync(CancellationToken cancellationToken = default);
    Task<List<DataTargetHealth>> GetDataTargetHealthAsync(CancellationToken cancellationToken = default);

    event EventHandler<SyncPipelineEventArgs>? OnPipelineStarted;
    event EventHandler<SyncPipelineEventArgs>? OnPipelineStopped;
    event EventHandler<SyncPipelineEventArgs>? OnPipelineError;
    event EventHandler<DataSyncEventArgs>? OnDataSynced;
}

/// <summary>
/// 同步管道配置
/// </summary>
public class SyncPipelineConfiguration
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 100;
    public string? FilterExpression { get; set; }
    public string RetryPolicy { get; set; } = "Exponential";
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// 同步管道
/// </summary>
public class SyncPipeline
{
    public string Name { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public SyncPipelineConfiguration Configuration { get; set; } = new();
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
    public SyncPipelineStatistics Statistics { get; set; } = new();
}

/// <summary>
/// 同步管道统计信息
/// </summary>
public class SyncPipelineStatistics
{
    public long SuccessfulChanges { get; set; }
    public long FailedChanges { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public DateTime? LastChangeProcessed { get; set; }
}

/// <summary>
/// 数据同步统计信息
/// </summary>
public class DataSyncStatistics
{
    public long TotalChangesReceived { get; set; }
    public long TotalChangesProcessed { get; set; }
    public long TotalErrors { get; set; }
    public int ActivePipelines { get; set; }
    public int TotalPipelines { get; set; }
    public int ActiveDataSources { get; set; }
    public int ActiveDataTargets { get; set; }
    public DateTime LastUpdated { get; set; }

    public double SuccessRate => TotalChangesReceived > 0 ? (double)TotalChangesProcessed / TotalChangesReceived : 0;
}

/// <summary>
/// 同步管道事件参数
/// </summary>
public class SyncPipelineEventArgs : EventArgs
{
    public string PipelineName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 数据同步事件参数
/// </summary>
public class DataSyncEventArgs : EventArgs
{
    public string PipelineName { get; set; } = string.Empty;
    public DatabaseChange Change { get; set; } = new();
    public DataWriteResult Result { get; set; } = new();
    public long ProcessingTimeMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}