using Microsoft.Extensions.Options;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using System.Text.Json;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// 复制事件处理器实现
    /// </summary>
    public class ReplicationEventProcessor : IReplicationEventProcessor
    {
        private readonly ILogger<ReplicationEventProcessor> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptions<LogicalReplicationOptions> _options;
        private readonly SemaphoreSlim _processingSemaphore = new(1, 1);
        private readonly EventProcessingStats _stats = new();

        public ReplicationEventProcessor(
            ILogger<ReplicationEventProcessor> logger,
            IServiceProvider serviceProvider,
            IOptions<LogicalReplicationOptions> options)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _options = options;
        }

        public async Task ProcessEventAsync(ReplicationEvent replicationEvent, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                if (!await _processingSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    _logger.LogWarning("事件处理超时，跳过事件: {EventType}/{Table}",
                        replicationEvent.EventType, replicationEvent.TableName);
                    return;
                }

                await ProcessSingleEventAsync(replicationEvent, cancellationToken);

                // 更新统计信息
                UpdateStats(replicationEvent, startTime, true);

                _logger.LogDebug("Successfully processed event: {EventType} on {Table} at {Time}",
                    replicationEvent.EventType, replicationEvent.TableName, replicationEvent.EventTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理复制事件失败: {EventType}/{Table}",
                    replicationEvent.EventType, replicationEvent.TableName);

                UpdateStats(replicationEvent, startTime, false);
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        public async Task ProcessEventsBatchAsync(IEnumerable<ReplicationEvent> events, CancellationToken cancellationToken = default)
        {
            var eventsList = events.ToList();
            if (!eventsList.Any()) return;

            var startTime = DateTime.UtcNow;
            var processedCount = 0;
            var failedCount = 0;

            _logger.LogInformation("开始批量处理 {Count} 个复制事件", eventsList.Count);

            foreach (var batch in eventsList.Chunk(_options.Value.BatchSize))
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                try
                {
                    await ProcessBatchInternalAsync(batch, dbContext, cancellationToken);
                    processedCount += batch.Length;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process events in batch, batch size: {BatchSize}", batch.Length);
                    failedCount += batch.Length;

                    // 尝试单独处理每个事件
                    foreach (var singleEvent in batch)
                    {
                        try
                        {
                            await ProcessSingleEventAsync(singleEvent, cancellationToken);
                            processedCount++;
                            failedCount--;
                        }
                        catch (Exception singleEx)
                        {
                            _logger.LogError(singleEx, "Failed to process individual event: {EventType}/{Table}",
                                singleEvent.EventType, singleEvent.TableName);
                        }
                    }
                }
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("批量处理完成 - 成功: {Processed}, 失败: {Failed}, 耗时: {Duration}ms",
                processedCount, failedCount, duration.TotalMilliseconds);
        }

        private async Task ProcessSingleEventAsync(ReplicationEvent replicationEvent, CancellationToken cancellationToken)
        {
            // 验证事件
            if (!IsValidEvent(replicationEvent))
            {
                _logger.LogWarning("跳过无效事件: {EventType}/{Table}",
                    replicationEvent.EventType, replicationEvent.TableName);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            switch (replicationEvent.TableName.ToUpperInvariant())
            {
                case "ORDERS":
                    await ProcessOrderEventAsync(replicationEvent, dbContext, cancellationToken);
                    break;

                case "OUTBOXEVENTS":
                    await ProcessOutboxEventAsync(replicationEvent, dbContext, cancellationToken);
                    break;

                default:
                    _logger.LogDebug("跳过未知表的事件: {Table}", replicationEvent.TableName);
                    break;
            }
        }

        private async Task ProcessBatchInternalAsync(
            ReplicationEvent[] batch,
            AppDbContext dbContext,
            CancellationToken cancellationToken)
        {
            var ordersEvents = batch.Where(e => e.TableName.Equals("ORDERS", StringComparison.OrdinalIgnoreCase));
            var outboxEvents = batch.Where(e => e.TableName.Equals("OUTBOXEVENTS", StringComparison.OrdinalIgnoreCase));

            // 批量处理订单事件
            foreach (var orderEvent in ordersEvents)
            {
                await ProcessOrderEventAsync(orderEvent, dbContext, cancellationToken);
            }

            // 批量处理外发箱事件
            foreach (var outboxEvent in outboxEvents)
            {
                await ProcessOutboxEventAsync(outboxEvent, dbContext, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task ProcessOrderEventAsync(ReplicationEvent replicationEvent, AppDbContext dbContext, CancellationToken cancellationToken)
        {
            // 这里可以实现具体的订单事件处理逻辑
            // 例如：触发通知、更新缓存、发送消息等
            _logger.LogDebug("处理订单事件: {EventType} - OrderId: {OrderId}",
                replicationEvent.EventType,
                replicationEvent.NewValues.GetValueOrDefault("Id")?.ToString());

            // 示例：如果是插入事件，可以触发通知
            if (replicationEvent.EventType.Equals("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                // 这里可以添加通知逻辑
                await Task.CompletedTask;
            }
        }

        private async Task ProcessOutboxEventAsync(ReplicationEvent replicationEvent, AppDbContext dbContext, CancellationToken cancellationToken)
        {
            // 处理外发箱事件
            _logger.LogDebug("处理外发箱事件: {EventType} - EventId: {EventId}",
                replicationEvent.EventType,
                replicationEvent.NewValues.GetValueOrDefault("Id")?.ToString());

            // 如果是删除事件，表示事件已被处理
            if (replicationEvent.EventType.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
            {
                // 可以添加清理逻辑或通知
                await Task.CompletedTask;
            }
        }

        private bool IsValidEvent(ReplicationEvent replicationEvent)
        {
            if (string.IsNullOrWhiteSpace(replicationEvent.EventType))
                return false;

            if (string.IsNullOrWhiteSpace(replicationEvent.TableName))
                return false;

            if (!_options.Value.ReplicatedTables.Contains(replicationEvent.TableName, StringComparer.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private void UpdateStats(ReplicationEvent replicationEvent, DateTime startTime, bool success)
        {
            var processingTime = DateTime.UtcNow - startTime;
            _stats.TotalEventsProcessed++;

            if (success)
            {
                _stats.AverageProcessingTimeMs = (_stats.AverageProcessingTimeMs * (_stats.TotalEventsProcessed - 1) + processingTime.TotalMilliseconds) / _stats.TotalEventsProcessed;
            }
            else
            {
                _stats.FailedEvents++;
            }

            // 按类型统计
            var eventType = replicationEvent.EventType;
            if (_stats.EventsByType.ContainsKey(eventType))
                _stats.EventsByType[eventType]++;
            else
                _stats.EventsByType[eventType] = 1;

            // 按表统计
            var tableName = replicationEvent.TableName;
            if (_stats.EventsByTable.ContainsKey(tableName))
                _stats.EventsByTable[tableName]++;
            else
                _stats.EventsByTable[tableName] = 1;

            _stats.LastProcessedEvent = DateTime.UtcNow;

            // 更新时间窗口统计
            UpdateTimeWindowStats();
        }

        private void UpdateTimeWindowStats()
        {
            var now = DateTime.UtcNow;
            var oneHourAgo = now.AddHours(-1);
            var today = now.Date;

            // 这里简化处理，实际实现中应该使用时间窗口缓存
            _stats.EventsProcessedLastHour = _stats.TotalEventsProcessed; // 简化
            _stats.EventsProcessedToday = _stats.TotalEventsProcessed;   // 简化
        }

        public Task<EventProcessingStats> GetProcessingStatsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_stats);
        }
    }
}