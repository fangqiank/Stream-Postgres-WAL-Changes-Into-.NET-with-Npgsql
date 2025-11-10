using Microsoft.AspNetCore.Mvc;
using Npgsql;
using DebeziumDemoApp.Services;

namespace DebeziumDemoApp.Extensions
{
    public static class HealthEndpoints
    {
        public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // GET /health
            endpoints.MapGet("/health", async (IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand("SELECT 1", conn);
                    await cmd.ExecuteScalarAsync();

                    return Results.Ok(new {
                        status = "healthy",
                        database = "connected",
                        timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Health check failed: {ex.Message}");
                }
            })
            .WithName("HealthCheck");

            // GET /health/backup
            endpoints.MapGet("/health/backup", async (IBackupPostgresService backupDb) =>
            {
                try
                {
                    var isHealthy = await backupDb.TestConnectionAsync();
                    if (isHealthy)
                    {
                        var productCount = await backupDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products");
                        return Results.Ok(new {
                            status = "healthy",
                            database = "connected",
                            productCount = productCount,
                            timestamp = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        return Results.Problem("Backup database connection failed");
                    }
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Backup health check failed: {ex.Message}");
                }
            })
            .WithName("BackupHealthCheck");

            // GET /api/database/initialize
            endpoints.MapPost("/api/database/initialize", async (IConfiguration configuration) =>
            {
                try
                {
                    var connectionString = configuration.GetConnectionString("PrimaryPostgres")
                        ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Create tables if they don't exist
                    var createTablesSql = @"
                        CREATE TABLE IF NOT EXISTS categories (
                            id SERIAL PRIMARY KEY,
                            name VARCHAR(100) NOT NULL,
                            description VARCHAR(255),
                            created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                            updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
                        );

                        CREATE TABLE IF NOT EXISTS products (
                            id SERIAL PRIMARY KEY,
                            name VARCHAR(255) NOT NULL,
                            price DECIMAL(10,2) NOT NULL,
                            description VARCHAR(500),
                            stock INTEGER DEFAULT 0,
                            created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                            updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
                        );

                        CREATE TABLE IF NOT EXISTS orders (
                            id SERIAL PRIMARY KEY,
                            order_number VARCHAR(100) NOT NULL UNIQUE,
                            customer_id INTEGER,
                            total_amount DECIMAL(10,2) NOT NULL,
                            status VARCHAR(50) DEFAULT 'pending',
                            order_date TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                            updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
                        );

                        -- Create indexes for better performance
                        CREATE INDEX IF NOT EXISTS idx_products_name ON products(name);
                        CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status);
                        CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(order_date);
                    ";

                    await using var cmd = new NpgsqlCommand(createTablesSql, conn);
                    await cmd.ExecuteNonQueryAsync();

                    return Results.Ok(new { message = "Database initialized successfully" });
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Database initialization failed: {ex.Message}");
                }
            })
            .WithName("InitializeDatabase");

            return endpoints;
        }
    }
}