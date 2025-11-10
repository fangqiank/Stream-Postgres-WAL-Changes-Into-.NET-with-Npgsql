using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Core.Services;
using DebeziumDemoApp.Core.DataSources;
using DebeziumDemoApp.Core.DataTargets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DebeziumDemoApp.Core.Extensions;

/// <summary>
/// 服务集合扩展，用于注册通用数据同步服务
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加通用数据同步服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddUniversalDataSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册核心接口和实现
        services.AddSingleton<IUniversalDataSyncService, UniversalDataSyncService>();
        services.AddHostedService<UniversalDataSyncService>(provider =>
            (UniversalDataSyncService)provider.GetRequiredService<IUniversalDataSyncService>());

        // 配置选项
        services.Configure<UniversalSyncOptions>(configuration.GetSection("UniversalSync"));

        return services;
    }

    /// <summary>
    /// 添加特定的数据源
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="name">数据源名称</param>
    /// <param name="dataSourceType">数据源类型</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDataSource(
        this IServiceCollection services,
        string name,
        DataSourceType dataSourceType)
    {
        services.AddSingleton<IDataSource>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetRequiredService<ILogger<DataSourceFactory>>();

            return dataSourceType switch
            {
                DataSourceType.Kafka => new KafkaDataSource(name, configuration,
                    provider.GetRequiredService<ILogger<KafkaDataSource>>()),
                DataSourceType.PostgreSQL => new PostgreSQLDataSource(name, configuration,
                    provider.GetRequiredService<ILogger<PostgreSQLDataSource>>()),
                DataSourceType.SQLServer => new SQLServerDataSource(name, configuration,
                    provider.GetRequiredService<ILogger<SQLServerDataSource>>()),
                DataSourceType.MongoDB => new MongoDBDataSource(name, configuration,
                    provider.GetRequiredService<ILogger<MongoDBDataSource>>()),
                _ => throw new ArgumentException($"Unsupported data source type: {dataSourceType}")
            };
        });

        return services;
    }

    /// <summary>
    /// 添加特定的数据目标
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="name">数据目标名称</param>
    /// <param name="dataTargetType">数据目标类型</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddDataTarget(
        this IServiceCollection services,
        string name,
        DataTargetType dataTargetType)
    {
        services.AddSingleton<IDataTarget>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            return dataTargetType switch
            {
                DataTargetType.PostgreSQL => new PostgreSQLDataTarget(name, configuration,
                    provider.GetRequiredService<ILogger<PostgreSQLDataTarget>>()),
                DataTargetType.SQLServer => new SQLServerDataTarget(name, configuration,
                    provider.GetRequiredService<ILogger<SQLServerDataTarget>>()),
                DataTargetType.MongoDB => new MongoDBDataTarget(name, configuration,
                    provider.GetRequiredService<ILogger<MongoDBDataTarget>>()),
                _ => throw new ArgumentException($"Unsupported data target type: {dataTargetType}")
            };
        });

        return services;
    }

    /// <summary>
    /// 添加数据同步管道
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="pipelineName">管道名称</param>
    /// <param name="sourceName">数据源名称</param>
    /// <param name="targetName">数据目标名称</param>
    /// <param name="configureAction">配置操作</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddSyncPipeline(
        this IServiceCollection services,
        string pipelineName,
        string sourceName,
        string targetName,
        Action<SyncPipelineConfiguration>? configureAction = null)
    {
        services.AddSingleton<ISyncPipelineInitializer>(provider =>
        {
            return new SyncPipelineInitializer(pipelineName, sourceName, targetName, configureAction);
        });

        return services;
    }

    /// <summary>
    /// 添加监控和健康检查
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddUniversalSyncMonitoring(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 添加健康检查服务
        services.AddHealthChecks()
            .AddCheck<UniversalSyncHealthCheck>("universal-sync");

        // 添加监控服务
        if (configuration.GetValue<bool>("Monitoring:EnablePrometheusMetrics", false))
        {
            services.AddMetrics();
        }

        return services;
    }

    /// <summary>
    /// 从配置文件自动配置数据源和目标
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddUniversalSyncFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 首先添加基础服务
        services.AddUniversalDataSync(configuration);

        // 从配置自动添加数据源
        var dataSourcesSection = configuration.GetSection("DataSources");
        foreach (var sourceSection in dataSourcesSection.GetChildren())
        {
            var name = sourceSection.Key;
            var typeString = sourceSection.GetValue<string>("Type");

            if (Enum.TryParse<DataSourceType>(typeString, out var dataSourceType))
            {
                services.AddDataSource(name, dataSourceType);
            }
        }

        // 从配置自动添加数据目标
        var dataTargetsSection = configuration.GetSection("DataTargets");
        foreach (var targetSection in dataTargetsSection.GetChildren())
        {
            var name = targetSection.Key;
            var typeString = targetSection.GetValue<string>("Type");

            if (Enum.TryParse<DataTargetType>(typeString, out var dataTargetType))
            {
                services.AddDataTarget(name, dataTargetType);
            }
        }

        // 添加监控
        services.AddUniversalSyncMonitoring(configuration);

        return services;
    }
}

/// <summary>
/// 通用数据同步选项
/// </summary>
public class UniversalSyncOptions
{
    public bool Enabled { get; set; } = true;
    public int HealthCheckIntervalSeconds { get; set; } = 30;
    public int StatisticsUpdateIntervalSeconds { get; set; } = 60;
    public int MaxConcurrentPipelines { get; set; } = 10;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableDistributedTracing { get; set; } = false;
}

/// <summary>
/// 数据源工厂
/// </summary>
public class DataSourceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataSourceFactory> _logger;

    public DataSourceFactory(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DataSourceFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public IDataSource CreateDataSource(string name, DataSourceType type)
    {
        return type switch
        {
            DataSourceType.Kafka => new KafkaDataSource(name, _configuration,
                _serviceProvider.GetRequiredService<ILogger<KafkaDataSource>>()),
            DataSourceType.PostgreSQL => new PostgreSQLDataSource(name, _configuration,
                _serviceProvider.GetRequiredService<ILogger<PostgreSQLDataSource>>()),
            DataSourceType.SQLServer => new SQLServerDataSource(name, _configuration,
                _serviceProvider.GetRequiredService<ILogger<SQLServerDataSource>>()),
            DataSourceType.MongoDB => new MongoDBDataSource(name, _configuration,
                _serviceProvider.GetRequiredService<ILogger<MongoDBDataSource>>()),
            _ => throw new ArgumentException($"Unsupported data source type: {type}")
        };
    }
}

/// <summary>
/// 数据目标工厂
/// </summary>
public class DataTargetFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataTargetFactory> _logger;

    public DataTargetFactory(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DataTargetFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public IDataTarget CreateDataTarget(string name, DataTargetType type)
    {
        return type switch
        {
            DataTargetType.PostgreSQL => new PostgreSQLDataTarget(name, _configuration,
                _serviceProvider.GetRequiredService<ILogger<PostgreSQLDataTarget>>()),
            DataTargetType.SQLServer => new SQLServerDataTarget(name, _configuration,
                _serviceProvider.GetRequiredService<ILogger<SQLServerDataTarget>>()),
            DataTargetType.MongoDB => new MongoDBDataTarget(name, _configuration,
                _serviceProvider.GetRequiredService<ILogger<MongoDBDataTarget>>()),
            _ => throw new ArgumentException($"Unsupported data target type: {type}")
        };
    }
}

/// <summary>
/// 同步管道初始化器接口
/// </summary>
public interface ISyncPipelineInitializer
{
    string PipelineName { get; }
    string SourceName { get; }
    string TargetName { get; }
    Action<SyncPipelineConfiguration>? ConfigureAction { get; }
}

/// <summary>
/// 同步管道初始化器实现
/// </summary>
public class SyncPipelineInitializer : ISyncPipelineInitializer
{
    public string PipelineName { get; }
    public string SourceName { get; }
    public string TargetName { get; }
    public Action<SyncPipelineConfiguration>? ConfigureAction { get; }

    public SyncPipelineInitializer(
        string pipelineName,
        string sourceName,
        string targetName,
        Action<SyncPipelineConfiguration>? configureAction)
    {
        PipelineName = pipelineName;
        SourceName = sourceName;
        TargetName = targetName;
        ConfigureAction = configureAction;
    }
}

/// <summary>
/// 通用数据同步健康检查
/// </summary>
public class UniversalSyncHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly IUniversalDataSyncService _syncService;
    private readonly ILogger<UniversalSyncHealthCheck> _logger;

    public UniversalSyncHealthCheck(
        IUniversalDataSyncService syncService,
        ILogger<UniversalSyncHealthCheck> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statistics = await _syncService.GetStatisticsAsync(cancellationToken);
            var sourceHealth = await _syncService.GetDataSourceHealthAsync(cancellationToken);
            var targetHealth = await _syncService.GetDataTargetHealthAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["TotalPipelines"] = statistics.TotalPipelines,
                ["ActivePipelines"] = statistics.ActivePipelines,
                ["ActiveDataSources"] = statistics.ActiveDataSources,
                ["ActiveDataTargets"] = statistics.ActiveDataTargets,
                ["TotalChangesReceived"] = statistics.TotalChangesReceived,
                ["TotalChangesProcessed"] = statistics.TotalChangesProcessed,
                ["SuccessRate"] = statistics.SuccessRate,
                ["HealthySources"] = sourceHealth.Count(h => h.IsHealthy),
                ["HealthyTargets"] = targetHealth.Count(h => h.IsHealthy)
            };

            var isHealthy = sourceHealth.All(h => h.IsHealthy) && targetHealth.All(h => h.IsHealthy);

            return isHealthy
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Universal data sync is healthy", data)
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("Some components are unhealthy", null, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for universal data sync");
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Health check failed", ex);
        }
    }
}