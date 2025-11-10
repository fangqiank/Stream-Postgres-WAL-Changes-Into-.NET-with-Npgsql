using System;
using System.Collections.Generic;
using System.Data;
using Npgsql;
using System.Threading.Tasks;

namespace UpdateTestScript
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 UPDATE操作同步测试开始");
            Console.WriteLine(new string('=', 50));

            // Neon数据库连接字符串
            string neonConnection = "Host=ep-rapid-wind-a5cne0p3-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_l1xO8KZ3vNa6;SSL Mode=Require;Trust Server Certificate=True;";

            try
            {
                await using var connection = new NpgsqlConnection(neonConnection);
                await connection.OpenAsync();

                Console.WriteLine("📡 连接到Neon数据库成功");

                // 步骤1: 查找现有的测试订单
                Console.WriteLine("\n🔍 查找现有的测试订单...");
                var orderId = await FindTestOrderAsync(connection);

                if (orderId == Guid.Empty)
                {
                    Console.WriteLine("❌ 未找到测试订单，创建新订单...");
                    orderId = await CreateTestOrderAsync(connection);
                    Console.WriteLine($"✅ 创建了新测试订单: {orderId}");
                }
                else
                {
                    Console.WriteLine($"✅ 找到现有测试订单: {orderId}");
                }

                // 步骤2: 显示订单当前状态
                var currentStatus = await GetOrderStatusAsync(connection, orderId);
                Console.WriteLine($"📋 当前订单状态: Status={currentStatus.Status}, Customer={currentStatus.CustomerName}");

                // 步骤3: 清空realtime_sync_status表
                Console.WriteLine("\n🧹 清空realtime_sync_status表...");
                await ClearSyncStatusTableAsync(connection);

                // 步骤4: 执行UPDATE操作
                var newStatus = $"UPDATED_{DateTime.UtcNow:HHmmss}";
                var newCustomerName = $"更新测试客户_{DateTime.UtcNow:HHmmss}";
                Console.WriteLine($"\n🔄 执行UPDATE操作: Status -> {newStatus}, CustomerName -> {newCustomerName}");

                await UpdateOrderAsync(connection, orderId, newStatus, newCustomerName);
                Console.WriteLine("✅ UPDATE操作完成");

                // 步骤5: 等待触发器执行
                Console.WriteLine("\n⏳ 等待触发器执行(3秒)...");
                await Task.Delay(3000);

                // 步骤6: 检查realtime_sync_status表
                Console.WriteLine("\n📊 检查realtime_sync_status表...");
                var syncRecords = await CheckSyncStatusAsync(connection);

                var updateRecordFound = false;
                foreach (var record in syncRecords)
                {
                    Console.WriteLine($"  - ID: {record.Id}, OrderId: {record.OrderId}, SyncType: {record.SyncType}, Time: {record.Time}");
                    if (record.OrderId == orderId && record.SyncType.ToUpper() == "UPDATE")
                    {
                        updateRecordFound = true;
                    }
                }

                // 步骤7: 验证结果
                Console.WriteLine("\n🎯 测试结果:");
                Console.WriteLine($"  UPDATE触发器触发: {(updateRecordFound ? "✅ 成功" : "❌ 失败")}");

                if (updateRecordFound)
                {
                    Console.WriteLine("🎉 UPDATE操作同步测试成功!");
                    Console.WriteLine("   - 触发器正确检测到UPDATE操作");
                    Console.WriteLine("   - 数据已写入realtime_sync_status表");
                    Console.WriteLine("   - 应用程序应该会在500ms内处理同步");
                }
                else
                {
                    Console.WriteLine("❌ UPDATE操作同步测试失败!");
                    Console.WriteLine("   - 触发器未检测到UPDATE操作");
                    Console.WriteLine("   - realtime_sync_status表中没有UPDATE记录");
                    Console.WriteLine("   - 可能的原因:");
                    Console.WriteLine("     * 触发器权限不足");
                    Console.WriteLine("     * 触发器逻辑有问题");
                    Console.WriteLine("     * 数据库连接问题");
                }

                // 步骤8: 额外测试 - 验证触发器是否正常工作
                Console.WriteLine("\n🔧 验证触发器状态...");
                await VerifyTriggersAsync(connection);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"🚨 测试过程中发生错误: {ex.Message}");
                Console.WriteLine($"   详细信息: {ex}");
            }

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("🏁 测试完成");
        }

        static async Task<Guid> FindTestOrderAsync(NpgsqlConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT \"Id\" FROM \"Orders\" WHERE \"Status\" = 'test_trigger' ORDER BY \"CreatedAt\" DESC LIMIT 1";

            var result = await cmd.ExecuteScalarAsync();
            return result != null ? (Guid)result : Guid.Empty;
        }

        static async Task<Guid> CreateTestOrderAsync(NpgsqlConnection connection)
        {
            var orderId = Guid.NewGuid();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ""Orders"" (""Id"", ""Amount"", ""CreatedAt"", ""CustomerName"", ""Status"", ""UpdatedAt"")
                VALUES (@id, @amount, @createdAt, @customerName, @status, @updatedAt)";

            cmd.Parameters.AddWithValue("@id", orderId);
            cmd.Parameters.AddWithValue("@amount", 99.99m);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@customerName", "UPDATE测试客户");
            cmd.Parameters.AddWithValue("@status", "test_trigger");
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

            await cmd.ExecuteNonQueryAsync();
            return orderId;
        }

        static async Task<(string Status, string CustomerName)> GetOrderStatusAsync(NpgsqlConnection connection, Guid orderId)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT \"Status\", \"CustomerName\" FROM \"Orders\" WHERE \"Id\" = @id";
            cmd.Parameters.AddWithValue("@id", orderId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return (reader.GetString(0), reader.GetString(1));
            }
            return ("Unknown", "Unknown");
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

        static async Task VerifyTriggersAsync(NpgsqlConnection connection)
        {
            Console.WriteLine("  检查触发器状态...");

            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT tgname, tgrelid::regclass as table_name, tgenabled
                FROM pg_trigger
                WHERE tgname LIKE '%order%'
                ORDER BY tgname";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var table = reader.GetString(1);
                var enabled = reader.GetBoolean(2);
                Console.WriteLine($"    触发器: {name} (表: {table}, 启用: {enabled})");
            }

            // 检查函数
            cmd.CommandText = @"
                SELECT proname, provolatile
                FROM pg_proc
                WHERE proname = 'trigger_realtime_sync'";

            var funcResult = await cmd.ExecuteScalarAsync();
            Console.WriteLine($"    触发器函数: {(funcResult != null ? "✅ 存在" : "❌ 不存在")}");
        }
    }
}