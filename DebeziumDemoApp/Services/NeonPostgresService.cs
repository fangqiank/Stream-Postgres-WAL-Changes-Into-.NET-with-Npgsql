using Microsoft.Extensions.Configuration;
using Npgsql;
using DebeziumDemoApp.Models;
using System.Data;

namespace DebeziumDemoApp.Services;

public interface INeonPostgresService
{
    Task<bool> TestConnectionAsync();
    Task<int> ExecuteNonQueryAsync(string query, params NpgsqlParameter[] parameters);
    Task<T?> ExecuteScalarAsync<T>(string query, params NpgsqlParameter[] parameters);
    Task<IEnumerable<T>> QueryAsync<T>(string query, Func<IDataReader, T> mapper, params NpgsqlParameter[] parameters);
    Task InitializeDatabaseAsync();
}

public class NeonPostgresService : INeonPostgresService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<NeonPostgresService> _logger;
    private readonly string _connectionString;

    public NeonPostgresService(IConfiguration configuration, ILogger<NeonPostgresService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("PrimaryPostgres")
            ?? throw new InvalidOperationException("PrimaryPostgres connection string not found");
    }

    private NpgsqlConnection GetConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = GetConnection();
            await connection.OpenAsync();

            using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync();

            _logger.LogInformation("Successfully connected to Neon PostgreSQL");
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Neon PostgreSQL");
            return false;
        }
    }

    public async Task<int> ExecuteNonQueryAsync(string query, params NpgsqlParameter[] parameters)
    {
        try
        {
            using var connection = GetConnection();
            await connection.OpenAsync();

            using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddRange(parameters);

            var result = await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Executed non-query: {Query}, affected rows: {Count}", query, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute non-query: {Query}", query);
            throw;
        }
    }

    public async Task<T?> ExecuteScalarAsync<T>(string query, params NpgsqlParameter[] parameters)
    {
        try
        {
            using var connection = GetConnection();
            await connection.OpenAsync();

            using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddRange(parameters);

            var result = await command.ExecuteScalarAsync();
            _logger.LogDebug("Executed scalar query: {Query}", query);

            return result == null ? default : (T)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute scalar query: {Query}", query);
            throw;
        }
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string query, Func<IDataReader, T> mapper, params NpgsqlParameter[] parameters)
    {
        try
        {
            using var connection = GetConnection();
            await connection.OpenAsync();

            using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddRange(parameters);

            using var reader = await command.ExecuteReaderAsync();
            var results = new List<T>();

            while (await reader.ReadAsync())
            {
                results.Add(mapper(reader));
            }

            _logger.LogDebug("Executed query: {Query}, returned {Count} rows", query, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query: {Query}", query);
            throw;
        }
    }

    public async Task InitializeDatabaseAsync()
    {
        _logger.LogInformation("Initializing database schema...");

        try
        {
            // Create tables if they don't exist
            var createTablesSql = @"
                CREATE TABLE IF NOT EXISTS categories (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    description VARCHAR(255),
                    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
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
                    customer_id INTEGER NOT NULL,
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

            await ExecuteNonQueryAsync(createTablesSql);

            // Insert sample data if tables are empty
            await InsertSampleDataAsync();

            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    private async Task InsertSampleDataAsync()
    {
        var categoryCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories");
        if (categoryCount == 0)
        {
            _logger.LogInformation("Inserting sample categories...");
            await ExecuteNonQueryAsync(
                @"INSERT INTO categories (name, description) VALUES
                    (@name1, @desc1), (@name2, @desc2), (@name3, @desc3)",
                new NpgsqlParameter("@name1", "Electronics"),
                new NpgsqlParameter("@desc1", "Electronic devices and accessories"),
                new NpgsqlParameter("@name2", "Books"),
                new NpgsqlParameter("@desc2", "Books and educational materials"),
                new NpgsqlParameter("@name3", "Clothing"),
                new NpgsqlParameter("@desc3", "Clothing and apparel")
            );
        }

        var productCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products");
        if (productCount == 0)
        {
            _logger.LogInformation("Inserting sample products...");
            await ExecuteNonQueryAsync(
                @"INSERT INTO products (name, price, description, stock) VALUES
                    (@name1, @price1, @desc1, @stock1),
                    (@name2, @price2, @desc2, @stock2),
                    (@name3, @price3, @desc3, @stock3)",
                new NpgsqlParameter("@name1", "Laptop"),
                new NpgsqlParameter("@price1", 999.99m),
                new NpgsqlParameter("@desc1", "High-performance laptop"),
                new NpgsqlParameter("@stock1", 50),
                new NpgsqlParameter("@name2", "Programming Book"),
                new NpgsqlParameter("@price2", 49.99m),
                new NpgsqlParameter("@desc2", "Learn to code effectively"),
                new NpgsqlParameter("@stock2", 100),
                new NpgsqlParameter("@name3", "T-Shirt"),
                new NpgsqlParameter("@price3", 19.99m),
                new NpgsqlParameter("@desc3", "Comfortable cotton t-shirt"),
                new NpgsqlParameter("@stock3", 200)
            );
        }
    }
}