using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;
using System.Text.Json;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

/// <summary>
/// Local order endpoints that use LocalDbContext for UI operations
/// These endpoints work with the replicated data from Neon
/// </summary>
public static class LocalOrderEndpoints
{
    public static void MapLocalOrderEndpoints(this WebApplication app)
    {
        // Local order endpoints use authentication
        var api = app.MapGroup("/api/local/orders").RequireAuthorization();

        // Get local order statistics
        api.MapGet("/stats", async (LocalDbContext db) =>
        {
            var totalOrders = await db.Orders.CountAsync();
            var todayOrders = await db.Orders.CountAsync(o => o.CreatedAt >= DateTime.UtcNow.Date);
            var totalAmount = await db.Orders.SumAsync(o => (double?)o.Amount ?? 0);
            var todayAmount = await db.Orders
                .Where(o => o.CreatedAt >= DateTime.UtcNow.Date)
                .SumAsync(o => (double?)o.Amount ?? 0);

            return Results.Ok(new
            {
                TotalOrders = totalOrders,
                TodayOrders = todayOrders,
                TotalAmount = totalAmount,
                TodayAmount = todayAmount
            });
        })
        .WithName("GetLocalOrderStats")
        .WithSummary("Get Local Order Statistics")
        .WithDescription("Get order statistics from local replicated database")
        .WithTags("LocalOrders");

        // Get local orders with pagination and search
        api.MapGet("/", async (
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
        .WithName("GetLocalOrders")
        .WithSummary("Get Local Orders List")
        .WithDescription("Get paginated list of orders from local replicated database with search functionality")
        .WithTags("LocalOrders");

        // Get single local order by ID
        api.MapGet("/{id}", async (Guid id, LocalDbContext db) =>
        {
            var order = await db.Orders.FindAsync(id);
            return order is not null ? Results.Ok(order) : Results.NotFound();
        })
        .WithName("GetLocalOrder")
        .WithSummary("Get Local Order by ID")
        .WithDescription("Get a specific order by its ID from local replicated database")
        .WithTags("LocalOrders");

        // Create new order (this will create in local and also generate CDC event)
        api.MapPost("/", async (CreateOrderRequest request, LocalDbContext db) =>
        {
            var order = new Order
            {
                CustomerName = request.CustomerName,
                Amount = request.Amount,
                Status = "Created",
            };

            db.Orders.Add(order);

            var eventData = new
            {
                AggregateType = "Order",
                AggregateId = order.Id.ToString(),
                EventType = "OrderCreated",
                Payload = new
                {
                    order.Id,
                    order.CustomerName,
                    order.Amount,
                    order.Status,
                    order.CreatedAt
                }
            };
            var outboxEvent = new LocalOutboxEvent
            {
                EventType = "OrderCreated",
                EventData = JsonSerializer.Serialize(eventData)
            };

            db.LocalOutboxEvents.Add(outboxEvent);
            await db.SaveChangesAsync();

            return Results.Created($"/api/local/orders/{order.Id}", order);
        })
        .WithName("CreateLocalOrder")
        .WithSummary("Create Local Order")
        .WithDescription("Create a new order in local database and generate corresponding CDC event")
        .WithTags("LocalOrders");

        // Update local order status
        api.MapPut("/{id}/status", async (Guid id, Microsoft.AspNetCore.Http.HttpRequest req, LocalDbContext db, ILogger<Program> logger) =>
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
                    EventData = JsonSerializer.Serialize(eventData)
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
        .WithName("UpdateLocalOrderStatus")
        .WithSummary("Update Local Order Status")
        .WithDescription("Update order status in local database and generate CDC event")
        .WithTags("LocalOrders");

        // Delete local order
        api.MapDelete("/{id}", async (Guid id, LocalDbContext db, ILogger<Program> logger) =>
        {
            try
            {
                var order = await db.Orders.FindAsync(id);
                if (order is null)
                {
                    return Results.NotFound();
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
                    EventData = JsonSerializer.Serialize(eventData)
                };

                // Remove order and add outbox event in the same transaction
                db.Orders.Remove(order);
                db.LocalOutboxEvents.Add(outboxEvent);

                await db.SaveChangesAsync();

                return Results.Ok(new { Message = "Order deleted successfully", DeletedOrder = orderDetails });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting local order");
                return Results.Problem($"Error deleting order: {ex.Message}");
            }
        })
        .WithName("DeleteLocalOrder")
        .WithSummary("Delete Local Order")
        .WithDescription("Delete an order from local database and generate CDC event")
        .WithTags("LocalOrders");

        // Get local CDC events for monitoring
        api.MapGet("/events", async (
            LocalDbContext db,
            int page = 1,
            int pageSize = 50,
            string? eventType = null) =>
        {
            var query = db.LocalOutboxEvents.AsQueryable();

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(e => e.EventType == eventType);
            }

            var totalCount = await query.CountAsync();

            var events = await query
                .OrderByDescending(e => e.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new
            {
                Events = events,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        })
        .WithName("GetLocalCdcEvents")
        .WithSummary("Get Local CDC Events")
        .WithDescription("Get paginated list of CDC events from local database with filtering")
        .WithTags("LocalOrders");

        // Get replication statistics
        api.MapGet("/replication-stats", async (LocalDbContext db) =>
        {
            var totalOrders = await db.Orders.CountAsync();
            var totalEvents = await db.LocalOutboxEvents.CountAsync();

            var recentOrders = await db.Orders
                .Where(o => o.CreatedAt >= DateTime.UtcNow.AddHours(-24))
                .CountAsync();

            var recentEvents = await db.LocalOutboxEvents
                .Where(e => e.CreatedAt >= DateTime.UtcNow.AddHours(-24))
                .CountAsync();

            var eventTypes = await db.LocalOutboxEvents
                .GroupBy(e => e.EventType)
                .Select(g => new { EventType = g.Key, Count = g.Count() })
                .ToListAsync();

            return Results.Ok(new
            {
                TotalOrders = totalOrders,
                TotalEvents = totalEvents,
                RecentOrders24h = recentOrders,
                RecentEvents24h = recentEvents,
                EventTypes = eventTypes
            });
        })
        .WithName("GetLocalReplicationStats")
        .WithSummary("Get Local Replication Statistics")
        .WithDescription("Get statistics about replicated data and CDC events from local database")
        .WithTags("LocalOrders");
    }
}