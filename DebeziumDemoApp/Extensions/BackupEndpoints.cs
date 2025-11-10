using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace DebeziumDemoApp.Extensions
{
    public static class BackupEndpoints
    {
        public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // GET /api/backup/products
            endpoints.MapGet("/api/backup/products", async (IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("BackupPostgres")
                        ?? throw new InvalidOperationException("BackupPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand("SELECT id, name, price, description, stock, created_at, updated_at FROM products ORDER BY created_at DESC", conn);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    var products = new List<object>();
                    while (await reader.ReadAsync())
                    {
                        products.Add(new {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            name = reader.GetString(reader.GetOrdinal("name")),
                            price = reader.GetDecimal(reader.GetOrdinal("price")),
                            description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                            stock = reader.GetInt32(reader.GetOrdinal("stock")),
                            created_at = reader.GetDateTime(reader.GetOrdinal("created_at")),
                            updated_at = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                        });
                    }

                    return Results.Ok(products);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error getting backup products: {ex.Message}");
                }
            })
            .WithName("GetBackupProducts");

            // GET /api/backup/orders
            endpoints.MapGet("/api/backup/orders", async (IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("BackupPostgres")
                        ?? throw new InvalidOperationException("BackupPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand("SELECT id, order_number, customer_id, total_amount, status, order_date, updated_at FROM orders ORDER BY order_date DESC", conn);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    var orders = new List<object>();
                    while (await reader.ReadAsync())
                    {
                        orders.Add(new {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            order_number = reader.GetString(reader.GetOrdinal("order_number")),
                            customer_id = reader.IsDBNull(reader.GetOrdinal("customer_id")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("customer_id")),
                            total_amount = reader.GetDecimal(reader.GetOrdinal("total_amount")),
                            status = reader.GetString(reader.GetOrdinal("status")),
                            order_date = reader.GetDateTime(reader.GetOrdinal("order_date")),
                            updated_at = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                        });
                    }

                    return Results.Ok(orders);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error getting backup orders: {ex.Message}");
                }
            })
            .WithName("GetBackupOrders");

            // GET /api/backup/categories
            endpoints.MapGet("/api/backup/categories", async (IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("BackupPostgres")
                        ?? throw new InvalidOperationException("BackupPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand("SELECT id, name, description, created_at, updated_at FROM categories ORDER BY name", conn);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    var categories = new List<object>();
                    while (await reader.ReadAsync())
                    {
                        categories.Add(new {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            name = reader.GetString(reader.GetOrdinal("name")),
                            description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                            created_at = reader.GetDateTime(reader.GetOrdinal("created_at")),
                            updated_at = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                        });
                    }

                    return Results.Ok(categories);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error getting backup categories: {ex.Message}");
                }
            })
            .WithName("GetBackupCategories");

            return endpoints;
        }
    }
}