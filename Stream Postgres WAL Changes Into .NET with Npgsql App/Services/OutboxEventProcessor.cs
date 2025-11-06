
using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    public class OutboxEventProcessor(
        IServiceProvider serviceProvider,
        ILogger<OutboxEventProcessor> logger
        ) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var unprocessedEvents = await dbContext.OutboxEvents
                        .Where(oe => !oe.Processed)
                        .OrderBy(oe => oe.CreatedAt)
                        .Take(100)
                        .ToListAsync();

                    if (unprocessedEvents.Count > 0)
                    {
                        logger.LogInformation("Found {Count} unprocessed events to process", unprocessedEvents.Count);
                    }

                    foreach (var outboxEvent in unprocessedEvents)
                    {
                        try
                        {
                            logger.LogInformation(
                                "Processing event: {EventType}, Aggregate: {AggregateType}/{AggregateId}, ID: {EventId}, Processed: {Processed}",
                                outboxEvent.EventType,
                                outboxEvent.AggregateType,
                                outboxEvent.AggregateId,
                                outboxEvent.Id,
                                outboxEvent.Processed
                                );

                            // Mark as processed with explicit state tracking
                            outboxEvent.Processed = true;
                            outboxEvent.ProcessedAt = DateTime.UtcNow;

                            // Force Entity Framework to track the changes
                            var entry = dbContext.Entry(outboxEvent);
                            entry.Property(e => e.Processed).IsModified = true;
                            entry.Property(e => e.ProcessedAt).IsModified = true;

                            logger.LogDebug("准备保存事件变更: {EventId}, Processed: {Processed}, ProcessedAt: {ProcessedAt}, Entity State: {EntityState}",
                                outboxEvent.Id, outboxEvent.Processed, outboxEvent.ProcessedAt, entry.State);

                            // Save immediately for each event to avoid batch issues
                            var saveResult = await dbContext.SaveChangesAsync(stoppingToken);

                            logger.LogInformation("事件已标记为已处理: {EventId}, SaveChanges result: {SaveResult}",
                                outboxEvent.Id, saveResult);

                            // Verify the changes were actually saved
                            if (saveResult == 0)
                            {
                                logger.LogWarning("SaveChanges返回0，尝试直接SQL更新: {EventId}", outboxEvent.Id);

                                // Fallback to direct SQL update
                                var sqlResult = await dbContext.Database.ExecuteSqlRawAsync(
                                    "UPDATE \"OutboxEvents\" SET \"Processed\" = true, \"ProcessedAt\" = {0} WHERE \"Id\" = {1}",
                                    DateTime.UtcNow, outboxEvent.Id);

                                logger.LogInformation("直接SQL更新结果: {EventId}, SQL result: {SqlResult}",
                                    outboxEvent.Id, sqlResult);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error processing event: {EventId}, Error details: {ErrorDetails}",
                                outboxEvent.Id, ex.Message);

                            // Reset the event state on error
                            try
                            {
                                var entry = dbContext.Entry(outboxEvent);
                                if (entry.State == EntityState.Modified)
                                {
                                    entry.Reload();
                                }
                            }
                            catch (Exception reloadEx)
                            {
                                logger.LogError(reloadEx, "重新加载事件状态失败: {EventId}", outboxEvent.Id);
                            }
                        }
                    }

                    if (unprocessedEvents.Count > 0)
                    {
                        logger.LogInformation("处理了 {Count} 个事件", unprocessedEvents.Count);
                    }

                    await Task.Delay(1000, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "处理外发箱事件时出错");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
    }
}
