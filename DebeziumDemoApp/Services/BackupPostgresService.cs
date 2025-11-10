using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DebeziumDemoApp.Services;

public class BackupPostgresService : IBackupPostgresService, IDisposable
{
    private readonly string _connectionString;
    private readonly NpgsqlConnection _connection;

    public BackupPostgresService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("BackupPostgres")
            ?? throw new InvalidOperationException("BackupPostgres connection string not configured.");
        _connection = new NpgsqlConnection(_connectionString);
    }

    public async Task InitializeDatabaseAsync()
    {
        try
        {
            await OpenConnectionAsync();

            // Create tables if they don't exist
            var createTablesSql = @"
                CREATE TABLE IF NOT EXISTS products (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    price DECIMAL(10,2) NOT NULL,
                    description TEXT,
                    stock INTEGER NOT NULL DEFAULT 0,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS orders (
                    id SERIAL PRIMARY KEY,
                    order_number VARCHAR(50) UNIQUE NOT NULL,
                    customer_id INTEGER,
                    total_amount DECIMAL(10,2) NOT NULL,
                    status VARCHAR(20) DEFAULT 'pending' CHECK (status IN ('pending', 'processing', 'shipped', 'delivered', 'cancelled')),
                    order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS categories (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(255) NOT NULL,
                    description TEXT,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );

                CREATE OR REPLACE FUNCTION update_updated_at_column()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.updated_at = CURRENT_TIMESTAMP;
                    RETURN NEW;
                END;
                $$ language 'plpgsql';

                DROP TRIGGER IF EXISTS update_products_updated_at ON products;
                CREATE TRIGGER update_products_updated_at
                    BEFORE UPDATE ON products
                    FOR EACH ROW
                    EXECUTE FUNCTION update_updated_at_column();

                DROP TRIGGER IF EXISTS update_orders_updated_at ON orders;
                CREATE TRIGGER update_orders_updated_at
                    BEFORE UPDATE ON orders
                    FOR EACH ROW
                    EXECUTE FUNCTION update_updated_at_column();

                DROP TRIGGER IF EXISTS update_categories_updated_at ON categories;
                CREATE TRIGGER update_categories_updated_at
                    BEFORE UPDATE ON categories
                    FOR EACH ROW
                    EXECUTE FUNCTION update_updated_at_column();
            ";

            await ExecuteNonQueryAsync(createTablesSql);
            Console.WriteLine("[BACKUP DB] Backup database initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BACKUP DB] Error initializing database: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await OpenConnectionAsync();
            var result = await QuerySingleAsync<int>("SELECT 1", reader => reader.GetInt32(0));
            return result == 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BACKUP DB] Connection test failed: {ex.Message}");
            return false;
        }
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await OpenConnectionAsync();

        using var command = new NpgsqlCommand(sql, _connection);
        command.Parameters.AddRange(parameters);

        return await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        await OpenConnectionAsync();

        using var command = new NpgsqlCommand(sql, _connection);
        command.Parameters.AddRange(parameters);

        var result = await command.ExecuteScalarAsync();
        return result == null ? default : (T)Convert.ChangeType(result, typeof(T));
    }

    public async Task<T> QuerySingleAsync<T>(string sql, Func<NpgsqlDataReader, T> mapper, params NpgsqlParameter[] parameters)
    {
        await OpenConnectionAsync();

        using var command = new NpgsqlCommand(sql, _connection);
        command.Parameters.AddRange(parameters);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return mapper(reader);
        }

        throw new InvalidOperationException("No rows returned");
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> mapper, params NpgsqlParameter[] parameters)
    {
        await OpenConnectionAsync();

        using var command = new NpgsqlCommand(sql, _connection);
        command.Parameters.AddRange(parameters);

        using var reader = await command.ExecuteReaderAsync();
        var results = new List<T>();

        while (await reader.ReadAsync())
        {
            results.Add(mapper(reader));
        }

        return results;
    }

    private async Task OpenConnectionAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}