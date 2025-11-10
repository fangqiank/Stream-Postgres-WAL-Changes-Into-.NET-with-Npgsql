using Microsoft.AspNetCore.Mvc;
using Npgsql;
using DebeziumDemoApp.Models;
using DebeziumDemoApp.Services;

namespace DebeziumDemoApp.Extensions
{
    public static class ProductsEndpoints
    {
        public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // GET /api/products
            endpoints.MapGet("/api/products", async (IConfiguration configuration) =>
            {
                var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                    ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand("SELECT * FROM products ORDER BY created_at DESC", conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                var products = new List<Product>();
                while (await reader.ReadAsync())
                {
                    products.Add(new Product
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("id")),
                        Name = reader.GetString(reader.GetOrdinal("name")),
                        Price = reader.GetDecimal(reader.GetOrdinal("price")),
                        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                        Stock = reader.GetInt32(reader.GetOrdinal("stock")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                    });
                }

                return Results.Ok(products);
            })
            .WithName("GetProducts");

            // POST /api/products
            endpoints.MapPost("/api/products", async (Product product, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    product.CreatedAt = DateTime.UtcNow;
                    product.UpdatedAt = DateTime.UtcNow;

                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand(@"
                        INSERT INTO products (name, price, description, stock, created_at, updated_at)
                        VALUES (@name, @price, @description, @stock, @createdAt, @updatedAt)
                        RETURNING id", conn);

                    cmd.Parameters.AddWithValue("@name", product.Name);
                    cmd.Parameters.AddWithValue("@price", product.Price);
                    cmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@stock", product.Stock);
                    cmd.Parameters.AddWithValue("@createdAt", product.CreatedAt);
                    cmd.Parameters.AddWithValue("@updatedAt", product.UpdatedAt);

                    product.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                    // Send real-time update
                    await realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
                    {
                        Schema = "public",
                        Table = "products",
                        Operation = "INSERT",
                        After = new Dictionary<string, object>
                        {
                            ["id"] = product.Id,
                            ["name"] = product.Name,
                            ["price"] = product.Price,
                            ["description"] = product.Description ?? (object)DBNull.Value,
                            ["stock"] = product.Stock,
                            ["created_at"] = product.CreatedAt,
                            ["updated_at"] = product.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.Created($"/api/products/{product.Id}", product);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error creating product: {ex.Message}");
                }
            })
            .WithName("CreateProduct");

            // PUT /api/products/{id}
            endpoints.MapPut("/api/products/{id}", async (int id, Product product, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Get existing product for "before" state
                    var existingProduct = await GetProductByIdAsync(id, conn);
                    if (existingProduct == null)
                    {
                        return Results.NotFound();
                    }

                    product.UpdatedAt = DateTime.UtcNow;

                    await using var cmd = new NpgsqlCommand(@"
                        UPDATE products
                        SET name = @name, price = @price, description = @description, stock = @stock, updated_at = @updatedAt
                        WHERE id = @id", conn);

                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@name", product.Name);
                    cmd.Parameters.AddWithValue("@price", product.Price);
                    cmd.Parameters.AddWithValue("@description", (object?)product.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@stock", product.Stock);
                    cmd.Parameters.AddWithValue("@updatedAt", product.UpdatedAt);

                    var rowsAffected = await cmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        return Results.NotFound();
                    }

                    product.Id = id;
                    product.CreatedAt = existingProduct.CreatedAt;

                    // Send real-time update
                    await realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
                    {
                        Schema = "public",
                        Table = "products",
                        Operation = "UPDATE",
                        Before = new Dictionary<string, object>
                        {
                            ["id"] = existingProduct.Id,
                            ["name"] = existingProduct.Name,
                            ["price"] = existingProduct.Price,
                            ["description"] = existingProduct.Description ?? (object)DBNull.Value,
                            ["stock"] = existingProduct.Stock,
                            ["created_at"] = existingProduct.CreatedAt,
                            ["updated_at"] = existingProduct.UpdatedAt
                        },
                        After = new Dictionary<string, object>
                        {
                            ["id"] = product.Id,
                            ["name"] = product.Name,
                            ["price"] = product.Price,
                            ["description"] = product.Description ?? (object)DBNull.Value,
                            ["stock"] = product.Stock,
                            ["created_at"] = product.CreatedAt,
                            ["updated_at"] = product.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.Ok(product);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error updating product: {ex.Message}");
                }
            })
            .WithName("UpdateProduct");

            // GET /api/products/{id}
            endpoints.MapGet("/api/products/{id}", async (int id, IConfiguration configuration) =>
            {
                var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                    ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var product = await GetProductByIdAsync(id, conn);
                if (product == null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(product);
            })
            .WithName("GetProductById");

            // DELETE /api/products/{id}
            endpoints.MapDelete("/api/products/{id}", async (int id, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Get existing product for "before" state
                    var existingProduct = await GetProductByIdAsync(id, conn);
                    if (existingProduct == null)
                    {
                        return Results.NotFound();
                    }

                    await using var cmd = new NpgsqlCommand("DELETE FROM products WHERE id = @id", conn);
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
                        Table = "products",
                        Operation = "DELETE",
                        Before = new Dictionary<string, object>
                        {
                            ["id"] = existingProduct.Id,
                            ["name"] = existingProduct.Name,
                            ["price"] = existingProduct.Price,
                            ["description"] = existingProduct.Description ?? (object)DBNull.Value,
                            ["stock"] = existingProduct.Stock,
                            ["created_at"] = existingProduct.CreatedAt,
                            ["updated_at"] = existingProduct.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error deleting product: {ex.Message}");
                }
            })
            .WithName("DeleteProduct");

            return endpoints;
        }

        private static async Task<Product?> GetProductByIdAsync(int id, NpgsqlConnection conn)
        {
            await using var cmd = new NpgsqlCommand("SELECT * FROM products WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Product
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Price = reader.GetDecimal(reader.GetOrdinal("price")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                    Stock = reader.GetInt32(reader.GetOrdinal("stock")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                };
            }

            return null;
        }
    }
}