using Microsoft.AspNetCore.Mvc;
using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Core.Services;

namespace DebeziumDemoApp.Extensions;

/// <summary>
/// Monitoring and streaming API endpoints
/// </summary>
public static class MonitoringApiEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Debezium connector status
        endpoints.MapGet("/api/debezium/connector", async () =>
        {
            // Return mock Debezium connector status
            var connectorStatus = new
            {
                name = "postgres-connector",
                state = "RUNNING",
                type = "source",
                worker_id = "worker-1",
                tasks = new[]
                {
                    new
                    {
                        id = 0,
                        state = "RUNNING",
                        worker_id = "worker-1",
                        time_lag = 1250,
                        event_count = 15420,
                        last_event_timestamp = DateTime.UtcNow.AddMilliseconds(-500)
                    }
                },
                uptime_seconds = 3600,
                last_heartbeat = DateTime.UtcNow,
                database = new
                {
                    server_name = "postgres-server",
                    database_name = "inventory",
                    table_count = 3,
                    lsn = 12345678
                }
            };

            return Results.Ok(connectorStatus);
        })
        .WithName("GetDebeziumConnectorStatus")
        .WithTags("Monitoring API");

        // Kafka topics
        endpoints.MapGet("/api/kafka/topics", async () =>
        {
            // Return mock Kafka topics
            var topics = new[]
            {
                new
                {
                    name = "debezium.public.products",
                    partition_count = 3,
                    replication_factor = 1,
                    message_count = 5240,
                    size_bytes = 2048576,
                    last_message_timestamp = DateTime.UtcNow.AddSeconds(-30)
                },
                new
                {
                    name = "debezium.public.orders",
                    partition_count = 3,
                    replication_factor = 1,
                    message_count = 3156,
                    size_bytes = 1024000,
                    last_message_timestamp = DateTime.UtcNow.AddMinutes(-2)
                },
                new
                {
                    name = "debezium.public.categories",
                    partition_count = 1,
                    replication_factor = 1,
                    message_count = 245,
                    size_bytes = 256000,
                    last_message_timestamp = DateTime.UtcNow.AddMinutes(-15)
                }
            };

            return Results.Ok(topics);
        })
        .WithName("GetKafkaTopics")
        .WithTags("Monitoring API");

        // CDC metrics
        endpoints.MapGet("/api/cdc/stats", async () =>
        {
            // Return mock CDC metrics
            var metrics = new
            {
                overview = new
                {
                    total_changes_captured = 8641,
                    changes_per_minute = 124,
                    avg_processing_time_ms = 15.2,
                    success_rate = 99.8,
                    last_change_timestamp = DateTime.UtcNow.AddSeconds(-10)
                },
                database_metrics = new
                {
                    lsn = 12345678,
                    lag_bytes = 1024,
                    tables_monitored = 3,
                    total_rows_affected = 8641
                },
                kafka_metrics = new
                {
                    messages_produced = 8641,
                    messages_per_minute = 124,
                    producer_lag_ms = 50,
                    topic_count = 3
                },
                recent_changes = new[]
                {
                    new { table = "products", operation = "UPDATE", timestamp = DateTime.UtcNow.AddSeconds(-10), record_id = 1 },
                    new { table = "orders", operation = "INSERT", timestamp = DateTime.UtcNow.AddSeconds(-45), record_id = 123 },
                    new { table = "categories", operation = "UPDATE", timestamp = DateTime.UtcNow.AddMinutes(-2), record_id = 3 }
                }
            };

            return Results.Ok(metrics);
        })
        .WithName("GetCdcStats")
        .WithTags("Monitoring API");

        // Server-Sent Events for real-time changes
        endpoints.MapGet("/api/changes/stream", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            var response = context.Response;
            response.Headers.Add("Content-Type", "text/event-stream");
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            // Simulate real-time change events
            var counter = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(3, 8)), cancellationToken);

                    var changeTypes = new[] { "INSERT", "UPDATE", "DELETE" };
                    var tables = new[] { "products", "orders", "categories" };
                    var changeType = changeTypes[Random.Shared.Next(changeTypes.Length)];
                    var table = tables[Random.Shared.Next(tables.Length)];

                    var eventData = $@"event: change
data: {{""id"":{++counter},""table"":""{table}"",""operation"":""{changeType}"",""timestamp"":""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}"",""record_id"":{Random.Shared.Next(1, 1000)}}}

";

                    await response.WriteAsync(eventData, cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        })
        .WithName("GetChangesStream")
        .WithTags("Monitoring API");

        return endpoints;
    }
}