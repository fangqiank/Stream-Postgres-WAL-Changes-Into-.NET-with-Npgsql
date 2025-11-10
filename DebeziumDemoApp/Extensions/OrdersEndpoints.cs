using Microsoft.AspNetCore.Mvc;
using Npgsql;
using DebeziumDemoApp.Models;
using DebeziumDemoApp.Services;

namespace DebeziumDemoApp.Extensions
{
    public static class OrdersEndpoints
    {
        public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // GET /api/orders
            endpoints.MapGet("/api/orders", async (IConfiguration configuration) =>
            {
                var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                    ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand("SELECT * FROM orders ORDER BY order_date DESC", conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                var orders = new List<Order>();
                while (await reader.ReadAsync())
                {
                    orders.Add(new Order
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("id")),
                        OrderNumber = reader.GetString(reader.GetOrdinal("order_number")),
                        CustomerId = reader.IsDBNull(reader.GetOrdinal("customer_id")) ? null : reader.GetInt32(reader.GetOrdinal("customer_id")),
                        TotalAmount = reader.GetDecimal(reader.GetOrdinal("total_amount")),
                        Status = reader.GetString(reader.GetOrdinal("status")),
                        OrderDate = reader.GetDateTime(reader.GetOrdinal("order_date")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                    });
                }

                return Results.Ok(orders);
            })
            .WithName("GetOrders");

            // POST /api/orders
            endpoints.MapPost("/api/orders", async (Order order, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    order.OrderDate = DateTime.UtcNow;
                    order.UpdatedAt = DateTime.UtcNow;

                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand(@"
                        INSERT INTO orders (order_number, customer_id, total_amount, status, order_date, updated_at)
                        VALUES (@orderNumber, @customerId, @totalAmount, @status, @orderDate, @updatedAt)
                        RETURNING id", conn);

                    cmd.Parameters.AddWithValue("@orderNumber", order.OrderNumber);
                    cmd.Parameters.AddWithValue("@customerId", (object?)order.CustomerId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@totalAmount", order.TotalAmount);
                    cmd.Parameters.AddWithValue("@status", order.Status);
                    cmd.Parameters.AddWithValue("@orderDate", order.OrderDate);
                    cmd.Parameters.AddWithValue("@updatedAt", order.UpdatedAt);

                    order.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                    // Send real-time update
                    await realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
                    {
                        Schema = "public",
                        Table = "orders",
                        Operation = "INSERT",
                        After = new Dictionary<string, object>
                        {
                            ["id"] = order.Id,
                            ["order_number"] = order.OrderNumber,
                            ["customer_id"] = order.CustomerId ?? (object)DBNull.Value,
                            ["total_amount"] = order.TotalAmount,
                            ["status"] = order.Status,
                            ["order_date"] = order.OrderDate,
                            ["updated_at"] = order.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.Created($"/api/orders/{order.Id}", order);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error creating order: {ex.Message}");
                }
            })
            .WithName("CreateOrder");

            // PUT /api/orders/{id}
            endpoints.MapPut("/api/orders/{id}", async (int id, Order order, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Get existing order for "before" state
                    var existingOrder = await GetOrderByIdAsync(id, conn);
                    if (existingOrder == null)
                    {
                        return Results.NotFound();
                    }

                    order.UpdatedAt = DateTime.UtcNow;

                    await using var cmd = new NpgsqlCommand(@"
                        UPDATE orders
                        SET order_number = @orderNumber, customer_id = @customerId, total_amount = @totalAmount,
                            status = @status, updated_at = @updatedAt
                        WHERE id = @id", conn);

                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@orderNumber", order.OrderNumber);
                    cmd.Parameters.AddWithValue("@customerId", (object?)order.CustomerId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@totalAmount", order.TotalAmount);
                    cmd.Parameters.AddWithValue("@status", order.Status);
                    cmd.Parameters.AddWithValue("@updatedAt", order.UpdatedAt);

                    var rowsAffected = await cmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        return Results.NotFound();
                    }

                    order.Id = id;
                    order.OrderDate = existingOrder.OrderDate;

                    // Send real-time update
                    await realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
                    {
                        Schema = "public",
                        Table = "orders",
                        Operation = "UPDATE",
                        Before = new Dictionary<string, object>
                        {
                            ["id"] = existingOrder.Id,
                            ["order_number"] = existingOrder.OrderNumber,
                            ["customer_id"] = existingOrder.CustomerId ?? (object)DBNull.Value,
                            ["total_amount"] = existingOrder.TotalAmount,
                            ["status"] = existingOrder.Status,
                            ["order_date"] = existingOrder.OrderDate,
                            ["updated_at"] = existingOrder.UpdatedAt
                        },
                        After = new Dictionary<string, object>
                        {
                            ["id"] = order.Id,
                            ["order_number"] = order.OrderNumber,
                            ["customer_id"] = order.CustomerId ?? (object)DBNull.Value,
                            ["total_amount"] = order.TotalAmount,
                            ["status"] = order.Status,
                            ["order_date"] = order.OrderDate,
                            ["updated_at"] = order.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.Ok(order);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error updating order: {ex.Message}");
                }
            })
            .WithName("UpdateOrder");

            // GET /api/orders/{id}
            endpoints.MapGet("/api/orders/{id}", async (int id, IConfiguration configuration) =>
            {
                var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                    ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var order = await GetOrderByIdAsync(id, conn);
                if (order == null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(order);
            })
            .WithName("GetOrderById");

            // DELETE /api/orders/{id}
            endpoints.MapDelete("/api/orders/{id}", async (int id, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Get existing order for "before" state
                    var existingOrder = await GetOrderByIdAsync(id, conn);
                    if (existingOrder == null)
                    {
                        return Results.NotFound();
                    }

                    await using var cmd = new NpgsqlCommand("DELETE FROM orders WHERE id = @id", conn);
                    cmd.Parameters.AddWithValue("@id", id);

                    var rowsAffected = await cmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        return Results.NotFound();
                    }

                    // Send real-time update
                    await realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
                    {
                        Schema = "public",
                        Table = "orders",
                        Operation = "DELETE",
                        Before = new Dictionary<string, object>
                        {
                            ["id"] = existingOrder.Id,
                            ["order_number"] = existingOrder.OrderNumber,
                            ["customer_id"] = existingOrder.CustomerId ?? (object)DBNull.Value,
                            ["total_amount"] = existingOrder.TotalAmount,
                            ["status"] = existingOrder.Status,
                            ["order_date"] = existingOrder.OrderDate,
                            ["updated_at"] = existingOrder.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error deleting order: {ex.Message}");
                }
            })
            .WithName("DeleteOrder");

            return endpoints;
        }

        private static async Task<Order?> GetOrderByIdAsync(int id, NpgsqlConnection conn)
        {
            await using var cmd = new NpgsqlCommand("SELECT * FROM orders WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Order
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    OrderNumber = reader.GetString(reader.GetOrdinal("order_number")),
                    CustomerId = reader.IsDBNull(reader.GetOrdinal("customer_id")) ? null : reader.GetInt32(reader.GetOrdinal("customer_id")),
                    TotalAmount = reader.GetDecimal(reader.GetOrdinal("total_amount")),
                    Status = reader.GetString(reader.GetOrdinal("status")),
                    OrderDate = reader.GetDateTime(reader.GetOrdinal("order_date")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                };
            }

            return null;
        }
    }
}