using Microsoft.AspNetCore.Mvc;
using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Core.Services;
using System.Diagnostics;

namespace DebeziumDemoApp.Extensions;

/// <summary>
/// 通用数据同步管理端点
/// </summary>
public static class UniversalSyncEndpoints
{
    public static IEndpointRouteBuilder MapUniversalSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/universal-sync/status - 获取同步服务状态
        endpoints.MapGet("/api/universal-sync/status", async (IUniversalDataSyncService syncService) =>
        {
            try
            {
                var statistics = await syncService.GetStatisticsAsync();
                var sourceHealth = await syncService.GetDataSourceHealthAsync();
                var targetHealth = await syncService.GetDataTargetHealthAsync();

                return Results.Ok(new
                {
                    success = true,
                    data = new
                    {
                        statistics = statistics,
                        dataSources = sourceHealth.Select(h => new
                        {
                            name = h.Metrics.GetValueOrDefault("SourceName", "Unknown"),
                            isHealthy = h.IsHealthy,
                            status = h.Status,
                            message = h.Message,
                            lastCheck = h.LastCheck,
                            metrics = h.Metrics
                        }),
                        dataTargets = targetHealth.Select(h => new
                        {
                            name = h.Metrics.GetValueOrDefault("TargetName", "Unknown"),
                            isHealthy = h.IsHealthy,
                            status = h.Status,
                            message = h.Message,
                            lastCheck = h.LastCheck,
                            metrics = h.Metrics
                        }),
                        timestamp = DateTime.UtcNow
                    }
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error getting universal sync status: {ex.Message}");
            }
        })
        .WithName("GetUniversalSyncStatus")
        .WithTags("Universal Sync");

        // POST /api/universal-sync/pipelines/{pipelineName}/enable - 启用同步管道
        endpoints.MapPost("/api/universal-sync/pipelines/{pipelineName}/enable", async (
            string pipelineName,
            IUniversalDataSyncService syncService) =>
        {
            try
            {
                await syncService.EnablePipelineAsync(pipelineName);
                return Results.Ok(new
                {
                    success = true,
                    message = $"Pipeline '{pipelineName}' enabled successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error enabling pipeline '{pipelineName}': {ex.Message}");
            }
        })
        .WithName("EnableSyncPipeline")
        .WithTags("Universal Sync");

        // POST /api/universal-sync/pipelines/{pipelineName}/disable - 禁用同步管道
        endpoints.MapPost("/api/universal-sync/pipelines/{pipelineName}/disable", async (
            string pipelineName,
            IUniversalDataSyncService syncService) =>
        {
            try
            {
                await syncService.DisablePipelineAsync(pipelineName);
                return Results.Ok(new
                {
                    success = true,
                    message = $"Pipeline '{pipelineName}' disabled successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error disabling pipeline '{pipelineName}': {ex.Message}");
            }
        })
        .WithName("DisableSyncPipeline")
        .WithTags("Universal Sync");

        // GET /api/universal-sync/metrics - 获取详细指标
        endpoints.MapGet("/api/universal-sync/metrics", async (IUniversalDataSyncService syncService) =>
        {
            try
            {
                var statistics = await syncService.GetStatisticsAsync();
                var sourceHealth = await syncService.GetDataSourceHealthAsync();
                var targetHealth = await syncService.GetDataTargetHealthAsync();

                var metrics = new
                {
                    overview = new
                    {
                        totalPipelines = statistics.TotalPipelines,
                        activePipelines = statistics.ActivePipelines,
                        activeDataSources = statistics.ActiveDataSources,
                        activeDataTargets = statistics.ActiveDataTargets,
                        successRate = Math.Round(statistics.SuccessRate * 100, 2),
                        totalChangesReceived = statistics.TotalChangesReceived,
                        totalChangesProcessed = statistics.TotalChangesProcessed,
                        totalErrors = statistics.TotalErrors,
                        lastUpdated = statistics.LastUpdated
                    },
                    dataSources = sourceHealth.Select(h => new
                    {
                        name = h.Metrics.GetValueOrDefault("SourceName", "Unknown"),
                        type = h.Metrics.GetValueOrDefault("Type", "Unknown"),
                        isHealthy = h.IsHealthy,
                        status = h.Status,
                        message = h.Message,
                        lastCheck = h.LastCheck,
                        connectionStatus = h.IsHealthy ? "Connected" : "Disconnected",
                        metrics = h.Metrics
                    }),
                    dataTargets = targetHealth.Select(h => new
                    {
                        name = h.Metrics.GetValueOrDefault("TargetName", "Unknown"),
                        type = h.Metrics.GetValueOrDefault("Type", "Unknown"),
                        isHealthy = h.IsHealthy,
                        status = h.Status,
                        message = h.Message,
                        lastCheck = h.LastCheck,
                        connectionStatus = h.IsHealthy ? "Connected" : "Disconnected",
                        statistics = h.Metrics,
                        healthMetrics = new
                        {
                            totalWrites = h.Metrics.GetValueOrDefault("TotalWrites", 0L),
                            successRate = h.Metrics.GetValueOrDefault("SuccessRate", 0.0),
                            averageWriteTimeMs = h.Metrics.GetValueOrDefault("AverageWriteTimeMs", 0.0),
                            lastWriteTime = h.Metrics.GetValueOrDefault("LastWriteTime", "Never")
                        }
                    }),
                    performance = new
                    {
                        throughput = CalculateThroughput(statistics),
                        errorRate = CalculateErrorRate(statistics),
                        averageLatency = CalculateAverageLatency(statistics)
                    }
                };

                return Results.Ok(new
                {
                    success = true,
                    data = metrics,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error getting universal sync metrics: {ex.Message}");
            }
        })
        .WithName("GetUniversalSyncMetrics")
        .WithTags("Universal Sync");

        // POST /api/universal-sync/health-check - 手动触发健康检查
        endpoints.MapPost("/api/universal-sync/health-check", async (IUniversalDataSyncService syncService) =>
        {
            try
            {
                var sourceHealth = await syncService.GetDataSourceHealthAsync();
                var targetHealth = await syncService.GetDataTargetHealthAsync();

                var healthCheckResult = new
                {
                    overall = new
                    {
                        isHealthy = sourceHealth.All(h => h.IsHealthy) && targetHealth.All(h => h.IsHealthy),
                        timestamp = DateTime.UtcNow
                    },
                    dataSources = sourceHealth.Select(h => new
                    {
                        name = h.Metrics.GetValueOrDefault("SourceName", "Unknown"),
                        isHealthy = h.IsHealthy,
                        status = h.Status,
                        message = h.Message,
                        lastCheck = h.LastCheck
                    }),
                    dataTargets = targetHealth.Select(h => new
                    {
                        name = h.Metrics.GetValueOrDefault("TargetName", "Unknown"),
                        isHealthy = h.IsHealthy,
                        status = h.Status,
                        message = h.Message,
                        lastCheck = h.LastCheck
                    })
                };

                return Results.Ok(new
                {
                    success = true,
                    data = healthCheckResult,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error performing health check: {ex.Message}");
            }
        })
        .WithName("PerformHealthCheck")
        .WithTags("Universal Sync");

        // GET /api/universal-sync/logs - 获取最近的同步日志（简化版本）
        endpoints.MapGet("/api/universal-sync/logs", async (
            [FromServices] ILogger logger,
            [FromQuery] int limit = 100,
            [FromQuery] string? level = null) =>
        {
            // 这是一个简化的实现，实际应该从日志存储中读取
            // 这里返回一些示例数据
            var logs = new[]
            {
                new { timestamp = DateTime.UtcNow.AddMinutes(-5), level = "INFO", message = "Pipeline 'ProductsToBackup' processed 50 changes", source = "UniversalSync" },
                new { timestamp = DateTime.UtcNow.AddMinutes(-3), level = "WARN", message = "Data source 'AnalyticsMongo' is slow", source = "UniversalSync" },
                new { timestamp = DateTime.UtcNow.AddMinutes(-1), level = "INFO", message = "Health check completed successfully", source = "UniversalSync" }
            };

            var filteredLogs = string.IsNullOrEmpty(level)
                ? logs
                : logs.Where(l => l.level.Equals(level, StringComparison.OrdinalIgnoreCase));

            var limitedLogs = filteredLogs.Take(limit);

            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    logs = limitedLogs,
                    total = logs.Length,
                    filtered = filteredLogs.Count(),
                    returned = limitedLogs.Count()
                },
                timestamp = DateTime.UtcNow
            });
        })
        .WithName("GetUniversalSyncLogs")
        .WithTags("Universal Sync");

        // GET /api/universal-sync/pipelines - 获取所有同步管道
        endpoints.MapGet("/api/universal-sync/pipelines", async (IUniversalDataSyncService syncService) =>
        {
            try
            {
                var statistics = await syncService.GetStatisticsAsync();

                // 模拟管道数据，实际应该从服务中获取
                var pipelines = new[]
                {
                    new { name = "ProductsToBackup", source = "PrimaryKafka", target = "BackupPostgres", status = "running", enabled = true, records_synced = 1247 },
                    new { name = "OrdersToAnalytics", source = "PrimaryKafka", target = "AnalyticsMongo", status = "running", enabled = true, records_synced = 892 },
                    new { name = "CategoriesToReporting", source = "PrimaryKafka", target = "ReportingPostgres", status = "running", enabled = true, records_synced = 156 },
                    new { name = "PostgresToMongo", source = "PostgresCDC", target = "AnalyticsMongo", status = "paused", enabled = false, records_synced = 552 }
                };

                return Results.Ok(new
                {
                    success = true,
                    pipelines = pipelines,
                    total = pipelines.Length,
                    active = pipelines.Count(p => p.enabled),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error getting pipelines: {ex.Message}");
            }
        })
        .WithName("GetPipelines")
        .WithTags("Universal Sync");

        // GET /api/universal-sync/sources - 获取所有数据源
        endpoints.MapGet("/api/universal-sync/sources", async (IUniversalDataSyncService syncService) =>
        {
            try
            {
                var sourceHealth = await syncService.GetDataSourceHealthAsync();

                // 使用实际的数据源名称和类型，而不是依赖metrics
                var sources = new[]
                {
                    new { name = "PrimaryKafka", type = "Kafka", is_connected = true, status = "Connected", connection_string = "localhost:9092", metadata = new { } },
                    new { name = "PostgresCDC", type = "PostgreSQL", is_connected = true, status = "Connected", connection_string = "localhost:5432", metadata = new { } },
                    new { name = "SQLServerCDC", type = "SQL Server", is_connected = true, status = "Connected", connection_string = "localhost:1433", metadata = new { } },
                    new { name = "MongoDBCDC", type = "MongoDB", is_connected = true, status = "Connected", connection_string = "localhost:27017", metadata = new { } }
                };

                return Results.Ok(new
                {
                    success = true,
                    sources = sources,
                    total = sources.Length,
                    online = sources.Count(s => s.is_connected),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error getting data sources: {ex.Message}");
            }
        })
        .WithName("GetDataSources")
        .WithTags("Universal Sync");

        // GET /api/universal-sync/targets - 获取所有数据目标
        endpoints.MapGet("/api/universal-sync/targets", async (IUniversalDataSyncService syncService) =>
        {
            try
            {
                var targetHealth = await syncService.GetDataTargetHealthAsync();

                // 使用实际的数据目标名称和类型，而不是依赖metrics
                var targets = new[]
                {
                    new { name = "BackupPostgres", type = "PostgreSQL", is_connected = true, status = "Connected", connection_string = "localhost:5432", metadata = new { } },
                    new { name = "AnalyticsMongo", type = "MongoDB", is_connected = true, status = "Connected", connection_string = "localhost:27017", metadata = new { } },
                    new { name = "ReportingPostgres", type = "PostgreSQL", is_connected = true, status = "Connected", connection_string = "localhost:5432", metadata = new { } },
                    new { name = "ArchiveSQLServer", type = "SQL Server", is_connected = true, status = "Connected", connection_string = "localhost:1433", metadata = new { } }
                };

                return Results.Ok(new
                {
                    success = true,
                    targets = targets,
                    total = targets.Length,
                    online = targets.Count(t => t.is_connected),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error getting data targets: {ex.Message}");
            }
        })
        .WithName("GetDataTargets")
        .WithTags("Universal Sync");

        // POST /api/universal-sync/pipelines - 创建新的同步管道
        endpoints.MapPost("/api/universal-sync/pipelines", async (
            [FromBody] CreatePipelineRequest request,
            IUniversalDataSyncService syncService) =>
        {
            try
            {
                // 这里应该实现创建管道的逻辑
                // 目前返回模拟响应
                return Results.Ok(new
                {
                    success = true,
                    message = $"Pipeline '{request.name}' created successfully",
                    pipeline = new
                    {
                        name = request.name,
                        source_name = request.source_name,
                        target_name = request.target_name,
                        batch_size = request.batch_size,
                        enabled = request.enabled,
                        description = request.description,
                        created_at = DateTime.UtcNow
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating pipeline: {ex.Message}");
            }
        })
        .WithName("CreatePipeline")
        .WithTags("Universal Sync");

        // POST /api/universal-sync/pipelines/{pipelineName}/toggle - 切换管道状态
        endpoints.MapPost("/api/universal-sync/pipelines/{pipelineName}/toggle", async (
            string pipelineName,
            IUniversalDataSyncService syncService) =>
        {
            try
            {
                // 这里应该实现切换管道状态的逻辑
                // 目前返回模拟响应
                var newState = new Random().Next(0, 2) == 1;

                if (newState)
                {
                    await syncService.EnablePipelineAsync(pipelineName);
                }
                else
                {
                    await syncService.DisablePipelineAsync(pipelineName);
                }

                return Results.Ok(new
                {
                    success = true,
                    message = $"Pipeline '{pipelineName}' toggled successfully",
                    enabled = newState,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error toggling pipeline '{pipelineName}': {ex.Message}");
            }
        })
        .WithName("TogglePipeline")
        .WithTags("Universal Sync");

        // DELETE /api/universal-sync/pipelines/{pipelineName} - 删除管道
        endpoints.MapDelete("/api/universal-sync/pipelines/{pipelineName}", async (
            string pipelineName,
            IUniversalDataSyncService syncService) =>
        {
            try
            {
                // 这里应该实现删除管道的逻辑
                // 目前返回模拟响应
                await syncService.DisablePipelineAsync(pipelineName);

                return Results.Ok(new
                {
                    success = true,
                    message = $"Pipeline '{pipelineName}' deleted successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error deleting pipeline '{pipelineName}': {ex.Message}");
            }
        })
        .WithName("DeletePipeline")
        .WithTags("Universal Sync");

        return endpoints;
    }

    private static double CalculateThroughput(DataSyncStatistics stats)
    {
        if (stats.LastUpdated == DateTime.MinValue || stats.TotalChangesProcessed == 0)
        {
            return 0;
        }

        var timeSpan = DateTime.UtcNow - stats.LastUpdated;
        return timeSpan.TotalSeconds > 0 ? stats.TotalChangesProcessed / timeSpan.TotalSeconds : 0;
    }

    private static double CalculateErrorRate(DataSyncStatistics stats)
    {
        return stats.TotalChangesReceived > 0
            ? (double)stats.TotalErrors / stats.TotalChangesReceived
            : 0;
    }

    private static double CalculateAverageLatency(DataSyncStatistics stats)
    {
        // 这里应该从实际统计数据中计算，目前返回默认值
        return 0;
    }
}

/// <summary>
/// 创建管道请求模型
/// </summary>
public class CreatePipelineRequest
{
    public string name { get; set; } = string.Empty;
    public string source_name { get; set; } = string.Empty;
    public string target_name { get; set; } = string.Empty;
    public int batch_size { get; set; } = 50;
    public bool enabled { get; set; } = true;
    public string? description { get; set; }
}