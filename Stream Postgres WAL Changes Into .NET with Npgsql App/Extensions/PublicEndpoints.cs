using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this WebApplication app)
    {
        // Test endpoint - no authentication required
        app.MapGet("/test", () => Results.Ok(new {
            Message = "API routing is working!",
            Timestamp = DateTime.UtcNow
        }))
        .WithName("Test")
        .WithSummary("Test Endpoint")
        .WithDescription("Simple test endpoint to verify API is working")
        .WithTags("Public");

        // Events endpoint - no authentication required (for display purposes)
        app.MapGet("/events", async (AppDbContext db) =>
        {
            var events = await db.OutboxEvents
                .OrderByDescending(e => e.CreatedAt)
                .Take(50)
                .Select(e => new
                {
                    e.Id,
                    e.EventType,
                    e.AggregateType,
                    e.AggregateId,
                    e.CreatedAt,
                    e.Processed,
                    e.ProcessedAt,
                    Payload = e.Payload != null && e.Payload.Length > 500 ? e.Payload.Substring(0, 500) + "..." : e.Payload
                })
                .ToListAsync();

            return Results.Ok(new {
                Events = events,
                TotalCount = await db.OutboxEvents.CountAsync(),
                UnprocessedCount = await db.OutboxEvents.CountAsync(e => !e.Processed),
                Timestamp = DateTime.UtcNow
            });
        })
        .WithName("GetEvents")
        .WithSummary("Get Outbox Events")
        .WithDescription("Get OutboxEvents for display (temporary workaround for processing issue)")
        .WithTags("Public");

        // API events endpoint - same as /events but with /api prefix for frontend compatibility
        app.MapGet("/api/events", async (AppDbContext db) =>
        {
            var events = await db.OutboxEvents
                .OrderByDescending(e => e.CreatedAt)
                .Take(50)
                .Select(e => new
                {
                    e.Id,
                    e.EventType,
                    e.AggregateType,
                    e.AggregateId,
                    e.CreatedAt,
                    e.Processed,
                    e.ProcessedAt,
                    Payload = e.Payload != null && e.Payload.Length > 500 ? e.Payload.Substring(0, 500) + "..." : e.Payload
                })
                .ToListAsync();

            return Results.Ok(new {
                Events = events,
                TotalCount = await db.OutboxEvents.CountAsync(),
                UnprocessedCount = await db.OutboxEvents.CountAsync(e => !e.Processed),
                Timestamp = DateTime.UtcNow
            });
        })
        .WithName("GetApiEvents")
        .WithSummary("Get Outbox Events (API)")
        .WithDescription("Get OutboxEvents for display with /api prefix")
        .WithTags("Public");

        // Order statistics endpoint - no authentication required (for public access)
        app.MapGet("/orders/stats", async (AppDbContext db) =>
        {
            var totalOrders = await db.Orders.CountAsync();
            var todayOrders = await db.Orders.CountAsync(o => o.CreatedAt >= DateTime.UtcNow.Date);
            var totalAmount = await db.Orders.SumAsync(o => (double?)o.Amount ?? 0);
            var todayAmount = await db.Orders
                .Where(o => o.CreatedAt >= DateTime.UtcNow.Date)
                .SumAsync(o => (double?)o.Amount ?? 0);

            // Status statistics for pie chart
            var statusStats = await db.Orders
                .GroupBy(o => o.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            // Trend data for line chart (last 7 days)
            var last7Days = Enumerable.Range(0, 7)
                .Select(i => DateTime.UtcNow.Date.AddDays(-i))
                .Reverse();

            var dailyTrends = await db.Orders
                .Where(o => o.CreatedAt >= DateTime.UtcNow.Date.AddDays(-7))
                .GroupBy(o => o.CreatedAt.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count(),
                    Amount = g.Sum(o => (double?)o.Amount ?? 0)
                })
                .ToDictionaryAsync(x => x.Date.Date, x => x);

            var trendData = last7Days.Select(date =>
            {
                if (dailyTrends.TryGetValue(date, out var data))
                    return (object)new {
                        date = date.ToString("MM-dd"),
                        count = data.Count,
                        amount = data.Amount
                    };
                return (object)new {
                    date = date.ToString("MM-dd"),
                    count = 0,
                    amount = 0m
                };
            }).Cast<object>().ToList();

            return Results.Ok(new
            {
                totalOrders = totalOrders,
                todayOrders = todayOrders,
                totalAmount = totalAmount,
                todayAmount = todayAmount,
                statusStats = statusStats,
                trendData = trendData
            });
        })
        .WithName("GetOrderStatsPublic")
        .WithSummary("Get Order Statistics (Public)")
        .WithDescription("Get order statistics without authentication")
        .WithTags("Public");

        // Root endpoint - serves the main page
        app.MapGet("/", async (HttpContext context) =>
        {
            try
            {
                var html = await System.IO.File.ReadAllTextAsync("wwwroot/index.html");
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(html);
            }
            catch
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "Page not found" });
            }
        })
        .WithName("Root")
        .WithSummary("Root Page")
        .WithDescription("Serve the main application page")
        .ExcludeFromDescription();
    }
}