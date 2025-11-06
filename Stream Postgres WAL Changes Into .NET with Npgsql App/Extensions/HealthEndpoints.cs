using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", async (AppDbContext db, IReplicationHealthMonitor healthMonitor) =>
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                var replicationHealth = await healthMonitor.GetHealthStatusAsync();

                var healthData = new
                {
                    Status = replicationHealth.IsHealthy && replicationHealth.SlotStatus != null ? "Healthy" : "Degraded",
                    timestamp = DateTime.UtcNow,
                    Database = "Connected",
                    Replication = new
                    {
                        IsHealthy = replicationHealth.IsHealthy,
                        SlotName = replicationHealth.Metrics.GetValueOrDefault("SlotName", "Unknown"),
                        PublicationName = replicationHealth.Metrics.GetValueOrDefault("PublicationName", "Unknown"),
                        ReplicationLagMs = replicationHealth.ReplicationLagMs,
                        LastChecked = replicationHealth.LastChecked,
                        Issues = replicationHealth.Issues
                    }
                };

                return Results.Ok(healthData);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: $"Health check failed: {ex.Message}",
                    statusCode: 503
                );
            }
        })
        .WithName("HealthCheck")
        .WithSummary("Health Check")
        .WithDescription("Check database connection and CDC replication status")
        .WithTags("Health");
    }
}