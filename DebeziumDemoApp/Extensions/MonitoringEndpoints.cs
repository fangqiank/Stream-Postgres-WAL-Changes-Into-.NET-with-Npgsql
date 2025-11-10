using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;
using DebeziumDemoApp.Services;

namespace DebeziumDemoApp.Extensions
{
    public static class MonitoringEndpoints
    {
        public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // GET /api/debezium/connector
            endpoints.MapGet("/api/debezium/connector", async () =>
            {
                try
                {
                    using var httpClientHandler = new HttpClientHandler()
                    {
                        Proxy = null,
                        UseProxy = false
                    };
                    using var httpClient = new HttpClient(httpClientHandler);
                    httpClient.Timeout = TimeSpan.FromSeconds(15);

                    var response = await httpClient.GetAsync("http://localhost:8083/connectors/debezium-postgres-connector/status");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var connectorData = JsonSerializer.Deserialize<JsonElement>(content);

                        return Results.Ok(new {
                            success = true,
                            data = connectorData,
                            timestamp = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        return Results.Ok(new {
                            success = false,
                            error = $"HTTP {response.StatusCode}",
                            timestamp = DateTime.UtcNow
                        });
                    }
                }
                catch (Exception ex)
                {
                    return Results.Ok(new {
                        success = false,
                        error = ex.Message,
                        timestamp = DateTime.UtcNow
                    });
                }
            })
            .WithName("DebeziumConnectorStatus");

            // GET /api/kafka/topics
            endpoints.MapGet("/api/kafka/topics", async () =>
            {
                try
                {
                    // Execute docker command to get Kafka topics
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "docker",
                            Arguments = "exec kafka bash -c '/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list'",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        return Results.Ok(new {
                            success = false,
                            error = error,
                            timestamp = DateTime.UtcNow
                        });
                    }

                    var topics = new List<object>();
                    if (!string.IsNullOrEmpty(output))
                    {
                        var topicLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var topic in topicLines)
                        {
                            var trimmedTopic = topic.Trim();
                            if (!string.IsNullOrEmpty(trimmedTopic))
                            {
                                topics.Add(new {
                                    name = trimmedTopic,
                                    partitions = 1, // Default for demo, could be enhanced to get actual partition count
                                    messageCount = 0 // Could be enhanced to get actual message count
                                });
                            }
                        }
                    }

                    return Results.Ok(new {
                        success = true,
                        data = topics,
                        timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    return Results.Ok(new {
                        success = false,
                        error = ex.Message,
                        timestamp = DateTime.UtcNow
                    });
                }
            })
            .WithName("KafkaTopicsStatus");

            // GET /api/cdc/stats
            endpoints.MapGet("/api/cdc/stats", async (ICDCMetricsService metricsService) =>
            {
                try
                {
                    var metrics = metricsService.GetCurrentMetrics();
                    return Results.Ok(metrics);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error getting CDC stats: {ex.Message}");
                }
            })
            .WithName("CDCStats");

            return endpoints;
        }
    }
}