using System;
using System.Data;
using Npgsql;
using System.Threading.Tasks;

namespace InlineUpdateTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🎯 内联UPDATE测试 - 针对用户订单 019a62dd-0d37-7622-9604-4fb2f710f403");
            Console.WriteLine(new string('=', 60));

            // Neon数据库连接字符串 (从应用程序配置中获取)
            string neonConnection = "Host=ep-rapid-wind-a5cne0p3-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_l1xO8KZ3vNa6;SSL Mode=Require;Trust Server Certificate=True;";

            try
            {
                await using var connection = new NpgsqlConnection(neonConnection);
                await connection.OpenAsync();
                Console.WriteLine("✅ 连接到Neon数据库成功");

                // 目标订单ID
                var targetOrderId = Guid.Parse("019a62dd-0d37-7622-9604-4fb2f710f403");

                // 1. 检查订单是否存在
                Console.WriteLine($"\n🔍 检查订单 {targetOrderId} 是否存在...");
                var orderExists = await CheckOrderExistsAsync(connection, targetOrderId);

                if (!orderExists)
                {
                    Console.WriteLine($"❌ 订单 {targetOrderId} 不存在，创建测试订单...");
                    await CreateTestOrderAsync(connection, targetOrderId);
                    Console.WriteLine($"✅ 测试订单 {targetOrderId} 已创建");
                }
                else
                {
                    Console.WriteLine($"✅ 订单 {targetOrderId} 存在");
                }

                // 2. 显示当前状态
                var currentStatus = await GetOrderStatusAsync(connection, targetOrderId);
                Console.WriteLine($"\n📋 当前订单状态: Status={currentStatus.Status}, Customer={currentStatus.CustomerName}, UpdatedAt={currentStatus.UpdatedAt}");

                // 3. 清空realtime_sync_status表
                Console.WriteLine("\n🧹 清空realtime_sync_status表...");
                await ClearSyncStatusTableAsync(connection);

                // 4. 执行UPDATE操作
                var newStatus = $"INLINE_TEST_{DateTime.UtcNow:HHmmss}";
                var newCustomerName = $"内联测试客户_{DateTime.UtcNow:HHmmss}";
                Console.WriteLine($"\n🔄 执行UPDATE操作: Status -> {newStatus}, CustomerName -> {newCustomerName}");

                await UpdateOrderAsync(connection, targetOrderId, newStatus, newCustomerName);
                Console.WriteLine("✅ UPDATE操作完成");

                // 5. 等待触发器执行
                Console.WriteLine("\n⏳ 等待触发器执行(3秒)...");
                await Task.Delay(3000);

                // 6. 检查realtime_sync_status表
                Console.WriteLine("\n📊 检查realtime_sync_status表...");
                var syncRecords = await CheckSyncStatusAsync(connection);

                var updateRecordFound = false;
                Console.WriteLine("Recent sync records:");
                foreach (var record in syncRecords)
                {
                    Console.WriteLine($"  - ID: {record.Id}, OrderId: {record.OrderId}, SyncType: {record.SyncType}, Time: {record.Time}");
                    if (record.OrderId == targetOrderId && record.SyncType.ToUpper() == "UPDATE")
                    {
                        updateRecordFound = true;
                    }
                }

                // 7. 验证结果
                Console.WriteLine("\n🎯 测试结果:");
                Console.WriteLine($"  UPDATE触发器触发: {(updateRecordFound ? "✅ 成功" : "❌ 失败")}");

                if (updateRecordFound)
                {
                    Console.WriteLine("🎉 内联UPDATE测试成功!");
                    Console.WriteLine("   - 触发器正确检测到UPDATE操作");
                    Console.WriteLine("   - 数据已写入realtime_sync_status表");
                    Console.WriteLine("   - 应用程序应该会在500ms内处理同步");
                    Console.WriteLine($"   - 目标订单: {targetOrderId}");
                }
                else
                {
                    Console.WriteLine("❌ 内联UPDATE测试失败!");
                    Console.WriteLine("   - 触发器未检测到UPDATE操作");
                    Console.WriteLine("   - realtime_sync_status表中没有UPDATE记录");
                }

                // 8. 再次确认订单状态
                var updatedStatus = await GetOrderStatusAsync(connection, targetOrderId);
                Console.WriteLine($"\n📋 更新后订单状态: Status={updatedStatus.Status}, Customer={updatedStatus.CustomerName}, UpdatedAt={updatedStatus.UpdatedAt}");

                // 9. 检查是否有其他订单的UPDATE记录
                Console.WriteLine("\n🔍 检查其他订单的UPDATE记录...");
                var otherUpdates = await CheckOtherUpdatesAsync(connection, targetOrderId);
                if (otherUpdates.Count > 0)
                {
                    Console.WriteLine("发现其他订单的UPDATE记录:");
                    foreach (var other in otherUpdates)
                    {
                        Console.WriteLine($"  - OrderId: {other.OrderId}, SyncType: {other.SyncType}, Time: {other.Time}");
                    }
                }
                else
                {
                    Console.WriteLine("没有发现其他订单的UPDATE记录");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"🚨 测试过程中发生错误: {ex.Message}");
                Console.WriteLine($"   详细信息: {ex}");
            }

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("🏁 内联测试完成");
        }

        static async Task<bool> CheckOrderExistsAsync(NpgsqlConnection connection, Guid orderId)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = @id";
            cmd.Parameters.AddWithValue("@id", orderId);

            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }

        static async Task CreateTestOrderAsync(NpgsqlConnection connection, Guid orderId)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ""Orders"" (""Id"", ""Amount"", ""CreatedAt"", ""CustomerName"", ""Status"", ""UpdatedAt"")
                VALUES (@id, @amount, @createdAt, @customerName, @status, @updatedAt)";

            cmd.Parameters.AddWithValue("@id", orderId);
            cmd.Parameters.AddWithValue("@amount", 199.99m);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@customerName", "用户测试订单");
            cmd.Parameters.AddWithValue("@status", "test_inline");
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

            await cmd.ExecuteNonQueryAsync();
        }

        static async Task<(string Status, string CustomerName, DateTime UpdatedAt)> GetOrderStatusAsync(NpgsqlConnection connection, Guid orderId)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT \"Status\", \"CustomerName\", \"UpdatedAt\" FROM \"Orders\" WHERE \"Id\" = @id";
            cmd.Parameters.AddWithValue("@id", orderId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return (reader.GetString(0), reader.GetString(1), reader.GetDateTime(2));
            }
            return ("Unknown", "Unknown", DateTime.MinValue);
        }

        static async Task ClearSyncStatusTableAsync(NpgsqlConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM realtime_sync_status";
            await cmd.ExecuteNonQueryAsync();
        }

        static async Task UpdateOrderAsync(NpgsqlConnection connection, Guid orderId, string newStatus, string newCustomerName)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE ""Orders""
                SET ""Status"" = @status, ""CustomerName"" = @customerName, ""UpdatedAt"" = @updatedAt
                WHERE ""Id"" = @id";

            cmd.Parameters.AddWithValue("@id", orderId);
            cmd.Parameters.AddWithValue("@status", newStatus);
            cmd.Parameters.AddWithValue("@customerName", newCustomerName);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

            await cmd.ExecuteNonQueryAsync();
        }

        static async Task<List<(int Id, Guid OrderId, string SyncType, DateTime Time)>> CheckSyncStatusAsync(NpgsqlConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, last_order_id, sync_type, last_sync_time
                FROM realtime_sync_status
                ORDER BY last_sync_time DESC
                LIMIT 10";

            var results = new List<(int, Guid, string, DateTime)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add((
                    reader.GetInt32(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetDateTime(3)
                ));
            }
            return results;
        }

        static async Task<List<(Guid OrderId, string SyncType, DateTime Time)>> CheckOtherUpdatesAsync(NpgsqlConnection connection, Guid excludeOrderId)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT last_order_id, sync_type, last_sync_time
                FROM realtime_sync_status
                WHERE last_order_id != @excludeOrderId AND sync_type = 'UPDATE'
                ORDER BY last_sync_time DESC
                LIMIT 5";

            cmd.Parameters.AddWithValue("@excludeOrderId", excludeOrderId);

            var results = new List<(Guid, string, DateTime)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetDateTime(2)
                ));
            }
            return results;
        }
    }
}