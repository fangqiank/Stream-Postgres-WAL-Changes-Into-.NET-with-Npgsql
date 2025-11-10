using Microsoft.AspNetCore.Mvc;
using Npgsql;
using DebeziumDemoApp.Models;
using DebeziumDemoApp.Services;

namespace DebeziumDemoApp.Extensions
{
    public static class CategoriesEndpoints
    {
        public static IEndpointRouteBuilder MapCategoriesEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // GET /api/categories
            endpoints.MapGet("/api/categories", async (IConfiguration configuration) =>
            {
                var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                    ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand("SELECT * FROM categories ORDER BY name", conn);
                await using var reader = await cmd.ExecuteReaderAsync();

                var categories = new List<Category>();
                while (await reader.ReadAsync())
                {
                    categories.Add(new Category
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("id")),
                        Name = reader.GetString(reader.GetOrdinal("name")),
                        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                    });
                }

                return Results.Ok(categories);
            })
            .WithName("GetCategories");

            // POST /api/categories
            endpoints.MapPost("/api/categories", async (Category category, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    category.CreatedAt = DateTime.UtcNow;
                    category.UpdatedAt = DateTime.UtcNow;

                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand(@"
                        INSERT INTO categories (name, description, created_at, updated_at)
                        VALUES (@name, @description, @createdAt, @updatedAt)
                        RETURNING id", conn);

                    cmd.Parameters.AddWithValue("@name", category.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)category.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@createdAt", category.CreatedAt);
                    cmd.Parameters.AddWithValue("@updatedAt", category.UpdatedAt);

                    category.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                    // Send real-time update
                    await realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
                    {
                        Schema = "public",
                        Table = "categories",
                        Operation = "INSERT",
                        After = new Dictionary<string, object>
                        {
                            ["id"] = category.Id,
                            ["name"] = category.Name,
                            ["description"] = category.Description ?? (object)DBNull.Value,
                            ["created_at"] = category.CreatedAt,
                            ["updated_at"] = category.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.Created($"/api/categories/{category.Id}", category);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error creating category: {ex.Message}");
                }
            })
            .WithName("CreateCategory");

            // PUT /api/categories/{id}
            endpoints.MapPut("/api/categories/{id}", async (int id, Category category, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Get existing category for "before" state
                    var existingCategory = await GetCategoryByIdAsync(id, conn);
                    if (existingCategory == null)
                    {
                        return Results.NotFound();
                    }

                    category.UpdatedAt = DateTime.UtcNow;

                    await using var cmd = new NpgsqlCommand(@"
                        UPDATE categories
                        SET name = @name, description = @description, updated_at = @updatedAt
                        WHERE id = @id", conn);

                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@name", category.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)category.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@updatedAt", category.UpdatedAt);

                    var rowsAffected = await cmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        return Results.NotFound();
                    }

                    category.Id = id;
                    category.CreatedAt = existingCategory.CreatedAt;

                    // Send real-time update
                    await realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
                    {
                        Schema = "public",
                        Table = "categories",
                        Operation = "UPDATE",
                        Before = new Dictionary<string, object>
                        {
                            ["id"] = existingCategory.Id,
                            ["name"] = existingCategory.Name,
                            ["description"] = existingCategory.Description ?? (object)DBNull.Value,
                            ["created_at"] = existingCategory.CreatedAt,
                            ["updated_at"] = existingCategory.UpdatedAt
                        },
                        After = new Dictionary<string, object>
                        {
                            ["id"] = category.Id,
                            ["name"] = category.Name,
                            ["description"] = category.Description ?? (object)DBNull.Value,
                            ["created_at"] = category.CreatedAt,
                            ["updated_at"] = category.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.Ok(category);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error updating category: {ex.Message}");
                }
            })
            .WithName("UpdateCategory");

            // GET /api/categories/{id}
            endpoints.MapGet("/api/categories/{id}", async (int id, IConfiguration configuration) =>
            {
                var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                    ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();

                var category = await GetCategoryByIdAsync(id, conn);
                if (category == null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(category);
            })
            .WithName("GetCategoryById");

            // DELETE /api/categories/{id}
            endpoints.MapDelete("/api/categories/{id}", async (int id, IRealtimeService realtimeService, IDataSyncService dataSyncService, IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Get existing category for "before" state
                    var existingCategory = await GetCategoryByIdAsync(id, conn);
                    if (existingCategory == null)
                    {
                        return Results.NotFound();
                    }

                    await using var cmd = new NpgsqlCommand("DELETE FROM categories WHERE id = @id", conn);
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
                        Table = "categories",
                        Operation = "DELETE",
                        Before = new Dictionary<string, object>
                        {
                            ["id"] = existingCategory.Id,
                            ["name"] = existingCategory.Name,
                            ["description"] = existingCategory.Description ?? (object)DBNull.Value,
                            ["created_at"] = existingCategory.CreatedAt,
                            ["updated_at"] = existingCategory.UpdatedAt
                        },
                        Timestamp = DateTime.UtcNow,
                        TransactionId = "",
                        Lsn = 0
                    });

                    return Results.NoContent();
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Error deleting category: {ex.Message}");
                }
            })
            .WithName("DeleteCategory");

            return endpoints;
        }

        private static async Task<Category?> GetCategoryByIdAsync(int id, NpgsqlConnection conn)
        {
            await using var cmd = new NpgsqlCommand("SELECT * FROM categories WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Category
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                    UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
                };
            }

            return null;
        }
    }
}