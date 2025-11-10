using Npgsql;
using Microsoft.Extensions.Configuration;
using DebeziumDemoApp.Models;

namespace DebeziumDemoApp.Services;

public class DataSyncService : IDataSyncService
{
    private readonly IBackupPostgresService _backupDb;
    private readonly IRealtimeService _realtimeService;
    private readonly bool _syncEnabled;

    public DataSyncService(IBackupPostgresService backupDb, IRealtimeService realtimeService, IConfiguration configuration)
    {
        _backupDb = backupDb;
        _realtimeService = realtimeService;
        _syncEnabled = configuration.GetValue<bool>("BackupSync:Enabled", true);
    }

    public async Task SyncChangeToBackupAsync(DatabaseChangeNotification change)
    {
        if (!_syncEnabled)
        {
            Console.WriteLine("[SYNC] Backup sync is disabled");
            return;
        }

        try
        {
            Console.WriteLine($"[SYNC] Synchronizing {change.Operation} on {change.Table} to backup database");

            switch (change.Table.ToLower())
            {
                case "products":
                    await SyncProductFromChangeAsync(change);
                    break;
                case "orders":
                    await SyncOrderFromChangeAsync(change);
                    break;
                case "categories":
                    await SyncCategoryFromChangeAsync(change);
                    break;
                default:
                    Console.WriteLine($"[SYNC] Unknown table: {change.Table}");
                    break;
            }

            // Broadcast sync completion
            await _realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
            {
                Operation = "SYNC_COMPLETED",
                Table = change.Table,
                Schema = "backup",
                Before = change.Before,
                After = change.After,
                Timestamp = DateTime.UtcNow,
                TransactionId = change.TransactionId + "_sync"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SYNC] Error synchronizing change: {ex.Message}");

            // Broadcast sync error
            await _realtimeService.BroadcastChangeAsync(new DatabaseChangeNotification
            {
                Operation = "SYNC_ERROR",
                Table = change.Table,
                Schema = "backup",
                Before = change.Before,
                After = change.After,
                Timestamp = DateTime.UtcNow,
                TransactionId = change.TransactionId + "_error"
            });
        }
    }

    private async Task SyncProductFromChangeAsync(DatabaseChangeNotification change)
    {
        if (change.After != null && change.Operation != "DELETE")
        {
            await SyncProductAsync(new Product
            {
                Id = SafeConvertToInt32(change.After["id"]),
                Name = Convert.ToString(change.After["name"]) ?? string.Empty,
                Price = SafeConvertToDecimal(change.After["price"]),
                Description = change.After["description"]?.ToString(),
                Stock = SafeConvertToInt32(change.After["stock"]),
                CreatedAt = ConvertTimestampToDateTime(change.After["created_at"]),
                UpdatedAt = ConvertTimestampToDateTime(change.After["updated_at"])
            }, change.Operation);
        }
        else if (change.Operation == "DELETE" && change.Before != null)
        {
            await _backupDb.ExecuteNonQueryAsync(
                "DELETE FROM products WHERE id = @id",
                new NpgsqlParameter("@id", SafeConvertToInt32(change.Before["id"])));
        }
    }

    private async Task SyncOrderFromChangeAsync(DatabaseChangeNotification change)
    {
        if (change.After != null && change.Operation != "DELETE")
        {
            await SyncOrderAsync(new Order
            {
                Id = SafeConvertToInt32(change.After["id"]),
                OrderNumber = Convert.ToString(change.After["order_number"]) ?? string.Empty,
                CustomerId = SafeConvertToNullableInt32(change.After["customer_id"]),
                TotalAmount = SafeConvertToDecimal(change.After["total_amount"]),
                Status = Convert.ToString(change.After["status"]) ?? string.Empty,
                OrderDate = ConvertTimestampToDateTime(change.After["order_date"]),
                UpdatedAt = ConvertTimestampToDateTime(change.After["updated_at"])
            }, change.Operation);
        }
        else if (change.Operation == "DELETE" && change.Before != null)
        {
            await _backupDb.ExecuteNonQueryAsync(
                "DELETE FROM orders WHERE id = @id",
                new NpgsqlParameter("@id", SafeConvertToInt32(change.Before["id"])));
        }
    }

    private async Task SyncCategoryFromChangeAsync(DatabaseChangeNotification change)
    {
        if (change.After != null && change.Operation != "DELETE")
        {
            await SyncCategoryAsync(new Category
            {
                Id = SafeConvertToInt32(change.After["id"]),
                Name = Convert.ToString(change.After["name"]) ?? string.Empty,
                Description = change.After["description"]?.ToString(),
                CreatedAt = ConvertTimestampToDateTime(change.After["created_at"]),
                UpdatedAt = ConvertTimestampToDateTime(change.After["updated_at"])
            }, change.Operation);
        }
        else if (change.Operation == "DELETE" && change.Before != null)
        {
            await _backupDb.ExecuteNonQueryAsync(
                "DELETE FROM categories WHERE id = @id",
                new NpgsqlParameter("@id", SafeConvertToInt32(change.Before["id"])));
        }
    }

    public async Task<Product> SyncProductAsync(Product product, string operation)
    {
        if (!_syncEnabled) return product;

        try
        {
            switch (operation.ToUpper())
            {
                case "INSERT":
                    await _backupDb.ExecuteNonQueryAsync(@"
                        INSERT INTO products (id, name, price, description, stock, created_at, updated_at)
                        VALUES (@id, @name, @price, @description, @stock, @created_at, @updated_at)
                        ON CONFLICT (id) DO NOTHING",
                        new NpgsqlParameter("@id", product.Id),
                        new NpgsqlParameter("@name", product.Name),
                        new NpgsqlParameter("@price", product.Price),
                        new NpgsqlParameter("@description", product.Description ?? (object)DBNull.Value),
                        new NpgsqlParameter("@stock", product.Stock),
                        new NpgsqlParameter("@created_at", product.CreatedAt),
                        new NpgsqlParameter("@updated_at", product.UpdatedAt));
                    break;

                case "UPDATE":
                    await _backupDb.ExecuteNonQueryAsync(@"
                        UPDATE products SET
                            name = @name,
                            price = @price,
                            description = @description,
                            stock = @stock,
                            updated_at = @updated_at
                        WHERE id = @id",
                        new NpgsqlParameter("@id", product.Id),
                        new NpgsqlParameter("@name", product.Name),
                        new NpgsqlParameter("@price", product.Price),
                        new NpgsqlParameter("@description", product.Description ?? (object)DBNull.Value),
                        new NpgsqlParameter("@stock", product.Stock),
                        new NpgsqlParameter("@updated_at", product.UpdatedAt));
                    break;
            }

            Console.WriteLine($"[SYNC] Successfully synced product {product.Id} to backup");
            return product;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SYNC] Error syncing product {product.Id}: {ex.Message}");
            throw;
        }
    }

    public async Task<Order> SyncOrderAsync(Order order, string operation)
    {
        if (!_syncEnabled) return order;

        try
        {
            switch (operation.ToUpper())
            {
                case "INSERT":
                    await _backupDb.ExecuteNonQueryAsync(@"
                        INSERT INTO orders (id, order_number, customer_id, total_amount, status, order_date, updated_at)
                        VALUES (@id, @order_number, @customer_id, @total_amount, @status, @order_date, @updated_at)
                        ON CONFLICT (id) DO NOTHING",
                        new NpgsqlParameter("@id", order.Id),
                        new NpgsqlParameter("@order_number", order.OrderNumber),
                        new NpgsqlParameter("@customer_id", order.CustomerId ?? (object)DBNull.Value),
                        new NpgsqlParameter("@total_amount", order.TotalAmount),
                        new NpgsqlParameter("@status", order.Status),
                        new NpgsqlParameter("@order_date", order.OrderDate),
                        new NpgsqlParameter("@updated_at", order.UpdatedAt));
                    break;

                case "UPDATE":
                    await _backupDb.ExecuteNonQueryAsync(@"
                        UPDATE orders SET
                            order_number = @order_number,
                            customer_id = @customer_id,
                            total_amount = @total_amount,
                            status = @status,
                            updated_at = @updated_at
                        WHERE id = @id",
                        new NpgsqlParameter("@id", order.Id),
                        new NpgsqlParameter("@order_number", order.OrderNumber),
                        new NpgsqlParameter("@customer_id", order.CustomerId ?? (object)DBNull.Value),
                        new NpgsqlParameter("@total_amount", order.TotalAmount),
                        new NpgsqlParameter("@status", order.Status),
                        new NpgsqlParameter("@updated_at", order.UpdatedAt));
                    break;
            }

            Console.WriteLine($"[SYNC] Successfully synced order {order.Id} to backup");
            return order;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SYNC] Error syncing order {order.Id}: {ex.Message}");
            throw;
        }
    }

    public async Task<Category> SyncCategoryAsync(Category category, string operation)
    {
        if (!_syncEnabled) return category;

        try
        {
            switch (operation.ToUpper())
            {
                case "INSERT":
                    await _backupDb.ExecuteNonQueryAsync(@"
                        INSERT INTO categories (id, name, description, created_at, updated_at)
                        VALUES (@id, @name, @description, @created_at, @updated_at)
                        ON CONFLICT (id) DO NOTHING",
                        new NpgsqlParameter("@id", category.Id),
                        new NpgsqlParameter("@name", category.Name),
                        new NpgsqlParameter("@description", category.Description ?? (object)DBNull.Value),
                        new NpgsqlParameter("@created_at", category.CreatedAt),
                        new NpgsqlParameter("@updated_at", category.UpdatedAt));
                    break;

                case "UPDATE":
                    await _backupDb.ExecuteNonQueryAsync(@"
                        UPDATE categories SET
                            name = @name,
                            description = @description,
                            updated_at = @updated_at
                        WHERE id = @id",
                        new NpgsqlParameter("@id", category.Id),
                        new NpgsqlParameter("@name", category.Name),
                        new NpgsqlParameter("@description", category.Description ?? (object)DBNull.Value),
                        new NpgsqlParameter("@updated_at", category.UpdatedAt));
                    break;
            }

            Console.WriteLine($"[SYNC] Successfully synced category {category.Id} to backup");
            return category;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SYNC] Error syncing category {category.Id}: {ex.Message}");
            throw;
        }
    }

    private static DateTime ConvertTimestampToDateTime(object timestamp)
    {
        if (timestamp == null || timestamp == DBNull.Value)
            return DateTime.UtcNow;

        try
        {
            // Handle Unix timestamp (milliseconds since epoch)
            if (timestamp is long longValue)
            {
                // Check if it's in milliseconds or seconds
                if (longValue > 9999999999999L) // milliseconds
                    return DateTimeOffset.FromUnixTimeMilliseconds(longValue).DateTime;
                else // seconds
                    return DateTimeOffset.FromUnixTimeSeconds(longValue).DateTime;
            }

            // Handle Unix timestamp as int
            if (timestamp is int intValue)
            {
                return DateTimeOffset.FromUnixTimeSeconds(intValue).DateTime;
            }

            // Handle Unix timestamp as string
            if (timestamp is string stringValue)
            {
                if (long.TryParse(stringValue, out long parsedLong))
                {
                    if (parsedLong > 9999999999999L) // milliseconds
                        return DateTimeOffset.FromUnixTimeMilliseconds(parsedLong).DateTime;
                    else // seconds
                        return DateTimeOffset.FromUnixTimeSeconds(parsedLong).DateTime;
                }
            }

            // If it's already a DateTime, return it
            if (timestamp is DateTime dateTimeValue)
                return dateTimeValue;

            // Default fallback
            return Convert.ToDateTime(timestamp);
        }
        catch (Exception)
        {
            // If all conversions fail, return current time
            return DateTime.UtcNow;
        }
    }

    private static int SafeConvertToInt32(object value)
    {
        return value != null && value != DBNull.Value ? Convert.ToInt32(value) : 0;
    }

    private static decimal SafeConvertToDecimal(object value)
    {
        return value != null && value != DBNull.Value ? Convert.ToDecimal(value) : 0m;
    }

    private static int? SafeConvertToNullableInt32(object value)
    {
        return value != null && value != DBNull.Value ? Convert.ToInt32(value) : null;
    }
}