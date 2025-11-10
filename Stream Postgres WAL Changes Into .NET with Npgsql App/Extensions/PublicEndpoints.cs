using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;

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

        // Configuration demonstration endpoint
        app.MapGet("/config-demo", (UserSecretsExample configExample) => {
            try
            {
                configExample.DemonstrateConfigurationReading();
                return Results.Ok(new {
                    Message = "Configuration demonstration completed - check application logs",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Configuration error: {ex.Message}");
            }
        })
        .WithName("ConfigDemo")
        .WithSummary("Configuration Demonstration")
        .WithDescription("Demonstrates how to read User Secrets and other configuration values")
        .WithTags("Public");

        // Events endpoint - no authentication required (for display purposes) - Using LocalDbContext
        app.MapGet("/events", async (LocalDbContext db) =>
        {
            // Fetch raw data first to avoid LINQ translation issues with computed properties
            var rawEvents = await db.LocalOutboxEvents
                .OrderByDescending(e => e.CreatedAt)
                .Take(50)
                .ToListAsync();

            // Project to DTO in memory to handle computed properties
            var events = rawEvents.Select(e => new
            {
                e.Id,
                e.EventType,
                AggregateType = e.AggregateType,
                AggregateId = e.AggregateId,
                e.CreatedAt,
                Processed = e.Processed,
                ProcessedAt = e.ProcessedAt,
                Payload = e.Payload != null && e.Payload.Length > 500 ? e.Payload.Substring(0, 500) + "..." : e.Payload
            }).ToList();

            // Get counts using simple properties to avoid SQL translation issues
            var totalCount = await db.LocalOutboxEvents.CountAsync();
            var unprocessedCount = await db.LocalOutboxEvents.CountAsync(e => !e.ProcessedAt.HasValue);

            return Results.Ok(new {
                Events = events,
                TotalCount = totalCount,
                UnprocessedCount = unprocessedCount,
                Timestamp = DateTime.UtcNow
            });
        })
        .WithName("GetEvents")
        .WithSummary("Get Outbox Events")
        .WithDescription("Get OutboxEvents from local database for display")
        .WithTags("Public");

        // API events endpoint - same as /events but with /api prefix for frontend compatibility - Using LocalDbContext
        app.MapGet("/api/events", async (LocalDbContext db) =>
        {
            // Fetch raw data first to avoid LINQ translation issues with computed properties
            var rawEvents = await db.LocalOutboxEvents
                .OrderByDescending(e => e.CreatedAt)
                .Take(50)
                .ToListAsync();

            // Project to DTO in memory to handle computed properties
            var events = rawEvents.Select(e => new
            {
                e.Id,
                e.EventType,
                AggregateType = e.AggregateType,
                AggregateId = e.AggregateId,
                e.CreatedAt,
                Processed = e.Processed,
                ProcessedAt = e.ProcessedAt,
                Payload = e.Payload != null && e.Payload.Length > 500 ? e.Payload.Substring(0, 500) + "..." : e.Payload
            }).ToList();

            // Get counts using simple properties to avoid SQL translation issues
            var totalCount = await db.LocalOutboxEvents.CountAsync();
            var unprocessedCount = await db.LocalOutboxEvents.CountAsync(e => !e.ProcessedAt.HasValue);

            return Results.Ok(new {
                Events = events,
                TotalCount = totalCount,
                UnprocessedCount = unprocessedCount,
                Timestamp = DateTime.UtcNow
            });
        })
        .WithName("GetApiEvents")
        .WithSummary("Get Outbox Events (API)")
        .WithDescription("Get OutboxEvents from local database for display with /api prefix")
        .WithTags("Public");

        // Neon events endpoint - queries the Neon database directly - Using AppDbContext
        app.MapGet("/neon/events", async (AppDbContext db) =>
        {
            try
            {
                // Fetch raw data from Neon database
                var rawEvents = await db.OutboxEvents
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(50)
                    .ToListAsync();

                // Project to DTO in memory to handle computed properties
                var events = rawEvents.Select(e => new
                {
                    e.Id,
                    e.EventType,
                    e.AggregateType,
                    e.AggregateId,
                    e.CreatedAt,
                    e.Processed,
                    e.ProcessedAt,
                    Payload = e.Payload != null && e.Payload.Length > 500 ? e.Payload.Substring(0, 500) + "..." : e.Payload
                }).ToList();

                // Get counts using simple properties to avoid SQL translation issues
                var totalCount = await db.OutboxEvents.CountAsync();
                var unprocessedCount = await db.OutboxEvents.CountAsync(e => !e.ProcessedAt.HasValue);

                return Results.Ok(new {
                    Events = events,
                    TotalCount = totalCount,
                    UnprocessedCount = unprocessedCount,
                    Timestamp = DateTime.UtcNow,
                    Source = "Neon Database (AppDbContext)"
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error accessing Neon database: {ex.Message}");
            }
        })
        .WithName("GetNeonEvents")
        .WithSummary("Get Neon Outbox Events")
        .WithDescription("Get OutboxEvents directly from Neon database for debugging")
        .WithTags("Debug");

        // Order statistics endpoint - no authentication required (for public access) - Using LocalDbContext
        app.MapGet("/orders/stats", async (LocalDbContext db) =>
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
        .WithDescription("Get order statistics from local database without authentication")
        .WithTags("Public");

        // Local orders endpoint for UI - using LocalDbContext
        app.MapGet("/orders", async (
            LocalDbContext db,
            int page = 1,
            int pageSize = 10,
            string? search = null) =>
        {
            var query = db.Orders.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(o => o.CustomerName.Contains(search) || o.Id.ToString().Contains(search));
            }

            var totalCount = await query.CountAsync();

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new
            {
                Orders = orders,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        })
        .WithName("GetLocalOrdersPublic")
        .WithSummary("Get Local Orders List")
        .WithDescription("Get paginated list of orders from local database for UI display")
        .WithTags("Public");

        // Delete order endpoint for UI - using LocalDbContext
        app.MapDelete("/orders/{id}", async (Guid id, LocalDbContext db, ILogger<Program> logger) =>
        {
            try
            {
                var order = await db.Orders.FindAsync(id);
                if (order is null)
                {
                    return Results.NotFound(new { error = "Order not found" });
                }

                // Store order details for CDC event before deletion
                var orderDetails = new
                {
                    order.Id,
                    order.CustomerName,
                    order.Amount,
                    order.Status,
                    order.CreatedAt,
                    order.UpdatedAt,
                    DeletedAt = DateTime.UtcNow
                };

                // Create outbox event for deletion
                var eventData = new
                {
                    AggregateType = "Order",
                    AggregateId = order.Id.ToString(),
                    EventType = "OrderDeleted",
                    Payload = orderDetails
                };
                var outboxEvent = new LocalOutboxEvent
                {
                    EventType = "OrderDeleted",
                    EventData = System.Text.Json.JsonSerializer.Serialize(eventData)
                };

                // Remove order and add outbox event in the same transaction
                db.Orders.Remove(order);
                db.LocalOutboxEvents.Add(outboxEvent);

                await db.SaveChangesAsync();

                return Results.Ok(new {
                    Message = "Order deleted successfully",
                    DeletedOrder = orderDetails,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting local order");
                return Results.Problem($"Error deleting order: {ex.Message}");
            }
        })
        .WithName("DeleteLocalOrderPublic")
        .WithSummary("Delete Local Order")
        .WithDescription("Delete an order from local database and generate CDC event")
        .WithTags("Public");

        // Update order status endpoint for UI - using LocalDbContext
        app.MapPut("/orders/{id}/status", async (Guid id, Microsoft.AspNetCore.Http.HttpRequest req, LocalDbContext db, ILogger<Program> logger) =>
        {
            try
            {
                var request = await req.ReadFromJsonAsync<UpdateOrderStatusRequest>();
                if (request?.Status == null)
                {
                    return Results.BadRequest("Status is required");
                }

                var order = await db.Orders.FindAsync(id);
                if (order is null)
                {
                    return Results.NotFound();
                }

                var oldStatus = order.Status;
                order.Status = request.Status;
                order.UpdatedAt = DateTime.UtcNow;

                db.Entry(order).State = EntityState.Modified;
                await db.SaveChangesAsync();

                var eventData = new
                {
                    AggregateType = "Order",
                    AggregateId = order.Id.ToString(),
                    EventType = "OrderStatusUpdated",
                    Payload = new
                    {
                        order.Id,
                        OldStatus = oldStatus,
                        NewStatus = request.Status,
                        UpdatedAt = order.UpdatedAt
                    }
                };
                var outboxEvent = new LocalOutboxEvent
                {
                    EventType = "OrderStatusUpdated",
                    EventData = System.Text.Json.JsonSerializer.Serialize(eventData)
                };

                db.LocalOutboxEvents.Add(outboxEvent);
                await db.SaveChangesAsync();

                return Results.Ok(order);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating local order status");
                return Results.Problem($"Error updating order: {ex.Message}");
            }
        })
        .WithName("UpdateLocalOrderStatusPublic")
        .WithSummary("Update Local Order Status")
        .WithDescription("Update order status in local database and generate CDC event")
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

        // Replication test endpoint - test PostgreSQL logical replication
        app.MapGet("/test-replication", async (IServiceProvider serviceProvider) =>
        {
            try
            {
                var replicationService = serviceProvider.GetRequiredService<PostgreSqlLogicalReplicationService>();
                var status = replicationService.GetStatus();

                return Results.Ok(new {
                    Message = "Replication status retrieved successfully",
                    Status = status,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Replication test failed: {ex.Message}");
            }
        })
        .WithName("TestReplication")
        .WithSummary("Test PostgreSQL Logical Replication")
        .WithDescription("Tests PostgreSQL logical replication by checking replication status")
        .WithTags("Public");
    }
}