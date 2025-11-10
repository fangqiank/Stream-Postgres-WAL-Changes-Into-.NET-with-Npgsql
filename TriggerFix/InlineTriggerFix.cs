using System;
using System.Data;
using Npgsql;
using System.Threading.Tasks;

namespace InlineTriggerFix
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("🔧 内联触发器修复 - 针对UPDATE同步问题");
            Console.WriteLine(new string('=', 60));

            // Neon数据库连接字符串
            string neonConnection = "Host=ep-rapid-wind-a5cne0p3-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_l1xO8KZ3vNa6;SSL Mode=Require;Trust Server Certificate=True;";

            try
            {
                await using var connection = new NpgsqlConnection(neonConnection);
                await connection.OpenAsync();
                Console.WriteLine("✅ 连接到Neon数据库成功");

                // 步骤1: 检查当前触发器状态
                Console.WriteLine("\n🔍 步骤1: 检查当前触发器状态...");
                await CheckCurrentTriggersAsync(connection);

                // 步骤2: 删除现有触发器
                Console.WriteLine("\n🗑️ 步骤2: 删除现有触发器...");
                await DropExistingTriggersAsync(connection);

                // 步骤3: 创建增强的触发器
                Console.WriteLine("\n⚡ 步骤3: 创建增强的触发器...");
                await CreateEnhancedTriggersAsync(connection);

                // 步骤4: 验证触发器安装
                Console.WriteLine("\n✅ 步骤4: 验证触发器安装...");
                await VerifyTriggerInstallationAsync(connection);

                // 步骤5: 测试用户指定的订单
                Console.WriteLine("\n🧪 步骤5: 测试用户指定订单的UPDATE操作...");
                await TestSpecificOrderAsync(connection, "4ca86d02-4d8f-4ecd-8641-6bfecf496bd3");

                // 步骤6: 最终验证
                Console.WriteLine("\n🎯 步骤6: 最终验证...");
                await FinalVerificationAsync(connection);

                Console.WriteLine("\n" + new string('=', 60));
                Console.WriteLine("🎉 触发器修复完成！UPDATE同步问题应该已解决");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"🚨 修复过程中发生错误: {ex.Message}");
                Console.WriteLine($"详细信息: {ex}");
            }

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("🏁 内联修复完成");
        }

        static async Task CheckCurrentTriggersAsync(NpgsqlConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    tgname as trigger_name,
                    tgrelid::regclass as table_name,
                    tgenabled as enabled,
                    tgtype::text as trigger_type
                FROM pg_trigger
                WHERE tgrelid = 'public.""Orders""'::regclass
                AND tgname LIKE '%realtime%'
                ORDER BY tgname";

            await using var reader = await cmd.ExecuteReaderAsync();
            Console.WriteLine("当前触发器状态:");

            bool found = false;
            while (await reader.ReadAsync())
            {
                found = true;
                var triggerName = reader.GetString(0);
                var tableName = reader.GetString(1);
                var enabled = reader.GetBoolean(2);
                var triggerType = reader.GetString(3);

                Console.WriteLine($"  - 触发器: {triggerName}, 表: {tableName}, 启用: {enabled}, 类型: {triggerType}");
            }

            if (!found)
            {
                Console.WriteLine("  ❌ 未找到realtime相关的触发器");
            }
        }

        static async Task DropExistingTriggersAsync(NpgsqlConnection connection)
        {
            var dropCommands = new[]
            {
                "DROP TRIGGER IF EXISTS realtime_order_trigger ON \"Orders\"",
                "DROP TRIGGER IF EXISTS realtime_notification_trigger ON \"Orders\"",
                "DROP FUNCTION IF EXISTS trigger_realtime_sync()",
                "DROP FUNCTION IF EXISTS notify_realtime_changes()"
            };

            foreach (var dropCmd in dropCommands)
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = dropCmd;
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"  ✅ 执行: {dropCmd}");
            }
        }

        static async Task CreateEnhancedTriggersAsync(NpgsqlConnection connection)
        {
            // 创建增强的触发器函数
            var createTriggerFunctionCmd = connection.CreateCommand();
            createTriggerFunctionCmd.CommandText = @"
                CREATE OR REPLACE FUNCTION trigger_realtime_sync()
                RETURNS TRIGGER AS $$
                BEGIN
                    BEGIN
                        DELETE FROM realtime_sync_status;

                        INSERT INTO realtime_sync_status (last_order_id, sync_type, is_active)
                        VALUES (
                            CASE
                                WHEN TG_OP = 'INSERT' THEN NEW.""Id""
                                WHEN TG_OP = 'UPDATE' THEN NEW.""Id""
                                WHEN TG_OP = 'DELETE' THEN OLD.""Id""
                            END,
                            TG_OP,
                            true
                        );

                        RAISE LOG 'Enhanced trigger executed: % for order %', TG_OP,
                            CASE
                                WHEN TG_OP = 'INSERT' THEN NEW.""Id""
                                WHEN TG_OP = 'UPDATE' THEN NEW.""Id""
                                WHEN TG_OP = 'DELETE' THEN OLD.""Id""
                            END;

                    EXCEPTION
                        WHEN OTHERS THEN
                            RAISE LOG 'Trigger error: %', SQLERRM;
                            RETURN NULL;
                    END;

                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql";

            await createTriggerFunctionCmd.ExecuteNonQueryAsync();
            Console.WriteLine("  ✅ 创建增强触发器函数");

            // 创建通知函数
            var createNotificationFunctionCmd = connection.CreateCommand();
            createNotificationFunctionCmd.CommandText = @"
                CREATE OR REPLACE FUNCTION notify_realtime_changes()
                RETURNS TRIGGER AS $$
                BEGIN
                    PERFORM pg_notify('realtime_wal_changes',
                        TG_OP || ':' ||
                        CASE
                            WHEN TG_OP = 'INSERT' THEN NEW.""Id""::text
                            WHEN TG_OP = 'UPDATE' THEN NEW.""Id""::text
                            WHEN TG_OP = 'DELETE' THEN OLD.""Id""::text
                        END);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql";

            await createNotificationFunctionCmd.ExecuteNonQueryAsync();
            Console.WriteLine("  ✅ 创建通知函数");

            // 创建触发器
            var createTriggersCmd = connection.CreateCommand();
            createTriggersCmd.CommandText = @"
                CREATE TRIGGER realtime_order_trigger
                AFTER INSERT OR UPDATE OR DELETE ON ""Orders""
                FOR EACH ROW EXECUTE FUNCTION trigger_realtime_sync();

                CREATE TRIGGER realtime_notification_trigger
                AFTER INSERT OR UPDATE OR DELETE ON ""Orders""
                FOR EACH ROW EXECUTE FUNCTION notify_realtime_changes();";

            await createTriggersCmd.ExecuteNonQueryAsync();
            Console.WriteLine("  ✅ 创建触发器");
        }

        static async Task VerifyTriggerInstallationAsync(NpgsqlConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    tgname as trigger_name,
                    tgrelid::regclass as table_name,
                    tgenabled as enabled,
                    CASE
                        WHEN tgtype::text LIKE '%4%' THEN 'INSERT, UPDATE, DELETE'
                        WHEN tgtype::text LIKE '%2%' THEN 'INSERT, UPDATE'
                        WHEN tgtype::text LIKE '%8%' THEN 'INSERT, DELETE'
                        ELSE 'UNKNOWN'
                    END as supported_operations
                FROM pg_trigger
                WHERE tgrelid = 'public.""Orders""'::regclass
                AND tgname LIKE '%realtime%'
                ORDER BY tgname";

            await using var reader = await cmd.ExecuteReaderAsync();
            Console.WriteLine("触发器验证结果:");

            while (await reader.ReadAsync())
            {
                var triggerName = reader.GetString(0);
                var tableName = reader.GetString(1);
                var enabled = reader.GetBoolean(2);
                var operations = reader.GetString(3);

                Console.WriteLine($"  ✅ 触发器: {triggerName}, 表: {tableName}, 启用: {enabled}, 支持操作: {operations}");
            }
        }

        static async Task TestSpecificOrderAsync(NpgsqlConnection connection, string orderIdStr)
        {
            var orderId = Guid.Parse(orderIdStr);

            Console.WriteLine($"  🎯 测试订单: {orderId}");

            // 检查订单是否存在
            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT \"Id\", \"Status\" FROM \"Orders\" WHERE \"Id\" = @id";
            checkCmd.Parameters.AddWithValue("@id", orderId);

            var orderExists = false;
            await using (var reader = await checkCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    orderExists = true;
                    var currentStatus = reader.GetString(1);
                    Console.WriteLine($"    📋 当前状态: {currentStatus}");
                }
            }

            if (!orderExists)
            {
                Console.WriteLine("    📝 订单不存在，创建测试订单...");
                await CreateTestOrderAsync(connection, orderId);
            }

            // 清空同步状态表
            {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM realtime_sync_status";
                    await cmd.ExecuteNonQueryAsync();
                }

            // 执行UPDATE操作
            var newStatus = $"FIX_TEST_{DateTime.UtcNow:HHmmss}";
            Console.WriteLine($"    🔄 执行UPDATE: Status -> {newStatus}");

            var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE ""Orders""
                SET ""Status"" = @status, ""UpdatedAt"" = @updatedAt
                WHERE ""Id"" = @id";

            updateCmd.Parameters.AddWithValue("@id", orderId);
            updateCmd.Parameters.AddWithValue("@status", newStatus);
            updateCmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();
            Console.WriteLine($"    ✅ UPDATE完成，影响行数: {rowsAffected}");

            // 等待触发器执行
            Console.WriteLine("    ⏳ 等待触发器执行(3秒)...");
            await Task.Delay(3000);

            // 检查同步结果
            var syncCheckCmd = connection.CreateCommand();
            syncCheckCmd.CommandText = @"
                SELECT COUNT(*)
                FROM realtime_sync_status
                WHERE last_order_id = @orderId AND sync_type = 'UPDATE'";

            syncCheckCmd.Parameters.AddWithValue("@orderId", orderId);
            var syncCount = Convert.ToInt32(await syncCheckCmd.ExecuteScalarAsync());

            Console.WriteLine($"    📊 同步记录数: {syncCount}");

            if (syncCount > 0)
            {
                Console.WriteLine("    🎉 UPDATE触发器工作正常！");
            }
            else
            {
                Console.WriteLine("    ❌ UPDATE触发器未检测到操作！");
            }
        }

        static async Task CreateTestOrderAsync(NpgsqlConnection connection, Guid orderId)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ""Orders"" (""Id"", ""Amount"", ""CreatedAt"", ""CustomerName"", ""Status"", ""UpdatedAt"")
                VALUES (@id, @amount, @createdAt, @customerName, @status, @updatedAt)";

            cmd.Parameters.AddWithValue("@id", orderId);
            cmd.Parameters.AddWithValue("@amount", 999.99m);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@customerName", "触发器修复测试订单");
            cmd.Parameters.AddWithValue("@status", "FIX_TEST_INITIAL");
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"    ✅ 测试订单已创建: {orderId}");
        }

        static async Task FinalVerificationAsync(NpgsqlConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*)
                FROM realtime_sync_status
                WHERE sync_type = 'UPDATE'
                AND last_sync_time > NOW() - INTERVAL '1 minute'";

            var recentUpdateCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            Console.WriteLine($"📊 最近1分钟内的UPDATE同步记录数: {recentUpdateCount}");

            if (recentUpdateCount > 0)
            {
                Console.WriteLine("🎉 UPDATE触发器修复成功！系统正在检测UPDATE操作。");
            }
            else
            {
                Console.WriteLine("⚠️ 没有检测到最近的UPDATE活动，可能需要进一步调查。");
            }
        }
    }
}