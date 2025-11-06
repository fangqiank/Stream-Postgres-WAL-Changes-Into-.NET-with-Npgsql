using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;
using System.Text.Json;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        // All order endpoints require authentication
        var api = app.MapGroup("/api/orders").RequireAuthorization();

        // Get order statistics
        api.MapGet("/stats", async (AppDbContext db) =>
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
        .WithName("GetOrderStats")
        .WithSummary("Get Order Statistics")
        .WithDescription("Get order statistics including daily trends")
        .WithTags("Orders");

        // Get orders with pagination and search
        api.MapGet("/", async (
            AppDbContext db,
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
        .WithName("GetOrders")
        .WithSummary("Get Orders List")
        .WithDescription("Get paginated list of orders with search functionality")
        .WithTags("Orders");

        // Get single order by ID
        api.MapGet("/{id}", async (Guid id, AppDbContext db) =>
        {
            var order = await db.Orders.FindAsync(id);
            return order is not null ? Results.Ok(order) : Results.NotFound();
        })
        .WithName("GetOrder")
        .WithSummary("Get Order by ID")
        .WithDescription("Get a specific order by its ID")
        .WithTags("Orders");

        // Create new order
        api.MapPost("/", async (CreateOrderRequest request, AppDbContext db) =>
        {
            var order = new Order
            {
                CustomerName = request.CustomerName,
                Amount = request.Amount,
                Status = "Created",
            };

            db.Orders.Add(order);

            var outboxEvent = new OutboxEvent
            {
                AggregateType = "Order",
                AggregateId = order.Id.ToString(),
                EventType = "OrderCreated",
                Payload = JsonSerializer.Serialize(new
                {
                    order.Id,
                    order.CustomerName,
                    order.Amount,
                    order.Status,
                    order.CreatedAt
                })
            };

            db.OutboxEvents.Add(outboxEvent);
            await db.SaveChangesAsync();

            return Results.Created($"/api/orders/{order.Id}", order);
        })
        .WithName("CreateOrder")
        .WithSummary("Create Order")
        .WithDescription("Create a new order and generate corresponding CDC event")
        .WithTags("Orders");

        // Update order status
        api.MapPut("/{id}/status", async (Guid id, Microsoft.AspNetCore.Http.HttpRequest req, AppDbContext db, ILogger<Program> logger) =>
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

                var outboxEvent = new OutboxEvent
                {
                    AggregateType = "Order",
                    AggregateId = order.Id.ToString(),
                    EventType = "OrderStatusUpdated",
                    Payload = JsonSerializer.Serialize(new
                    {
                        order.Id,
                        OldStatus = oldStatus,
                        NewStatus = request.Status,
                        UpdatedAt = order.UpdatedAt
                    })
                };

                db.OutboxEvents.Add(outboxEvent);
                await db.SaveChangesAsync();

                return Results.Ok(order);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating order status");
                return Results.Problem($"Error updating order: {ex.Message}");
            }
        })
        .WithName("UpdateOrderStatus")
        .WithSummary("Update Order Status")
        .WithDescription("Update order status and generate CDC event")
        .WithTags("Orders");
    }
}