# PowerShell脚本：直接修复UPDATE触发器问题
Write-Host "🔧 开始修复UPDATE触发器问题" -ForegroundColor Green
Write-Host "目标订单: 4ca86d02-4d8f-4ecd-8641-6bfecf496bd3" -ForegroundColor Yellow

# 数据库连接字符串
$connectionString = "Host=ep-rapid-wind-a5cne0p3-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_l1xO8KZ3vNa6;SSL Mode=Require;Trust Server Certificate=True;"

try {
    # 加载Npgsql
    $npgsqlPath = "D:\dotnetcore\Stream Postgres WAL Changes Into .NET with Npgsql App\Stream Postgres WAL Changes Into .NET with Npgsql App\bin\Debug\net9.0"
    if (Test-Path "$npgsqlPath\Npgsql.dll") {
        Add-Type -Path "$npgsqlPath\Npgsql.dll"
        Write-Host "✅ 已加载Npgsql.dll" -ForegroundColor Green
    } else {
        # 尝试从全局NuGet包加载
        try {
            Install-Package -Name Npgsql -Scope CurrentUser -Force -ErrorAction SilentlyContinue
            $globalPackages = Get-Package -ListAvailable | Where-Object { $_.Name -eq "Npgsql" } | Select-Object -First 1
            if ($globalPackages) {
                $dllPath = Join-Path $globalPackages.Source "lib\netstandard2.0\Npgsql.dll"
                if (Test-Path $dllPath) {
                    Add-Type -Path $dllPath
                    Write-Host "✅ 已加载Npgsql.dll" -ForegroundColor Green
                }
            }
        } catch {
            Write-Host "❌ 无法加载Npgsql，尝试其他方法" -ForegroundColor Red
        }
    }

    # 创建数据库连接
    Write-Host "📡 连接到数据库..." -ForegroundColor Cyan
    $connection = New-Object Npgsql.NpgsqlConnection($connectionString)
    $connection.Open()

    Write-Host "✅ 数据库连接成功" -ForegroundColor Green

    # 步骤1: 删除现有触发器
    Write-Host "`n🗑️ 步骤1: 删除现有触发器..." -ForegroundColor Yellow
    $dropTriggers = @(
        "DROP TRIGGER IF EXISTS realtime_order_trigger ON ""Orders""",
        "DROP TRIGGER IF EXISTS realtime_notification_trigger ON ""Orders""",
        "DROP FUNCTION IF EXISTS trigger_realtime_sync()",
        "DROP FUNCTION IF EXISTS notify_realtime_changes()"
    )

    foreach ($sql in $dropTriggers) {
        try {
            $cmd = $connection.CreateCommand()
            $cmd.CommandText = $sql
            $cmd.ExecuteNonQuery()
            Write-Host "  ✅ $sql" -ForegroundColor Gray
        } catch {
            Write-Host "  ⚠️ $sql (可能不存在)" -ForegroundColor Yellow
        }
    }

    # 步骤2: 创建增强的触发器函数
    Write-Host "`n⚡ 步骤2: 创建增强的触发器函数..." -ForegroundColor Yellow

    $createTriggerFunction = @"
        CREATE OR REPLACE FUNCTION trigger_realtime_sync()
        RETURNS TRIGGER AS `$$
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
        `$$ LANGUAGE plpgsql;
"@

    $cmd = $connection.CreateCommand()
    $cmd.CommandText = $createTriggerFunction
    $cmd.ExecuteNonQuery()
    Write-Host "  ✅ 创建增强触发器函数" -ForegroundColor Green

    # 步骤3: 创建通知函数
    Write-Host "`n📢 步骤3: 创建通知函数..." -ForegroundColor Yellow

    $createNotificationFunction = @"
        CREATE OR REPLACE FUNCTION notify_realtime_changes()
        RETURNS TRIGGER AS `$$
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
        `$$ LANGUAGE plpgsql;
"@

    $cmd = $connection.CreateCommand()
    $cmd.CommandText = $createNotificationFunction
    $cmd.ExecuteNonQuery()
    Write-Host "  ✅ 创建通知函数" -ForegroundColor Green

    # 步骤4: 创建触发器
    Write-Host "`n🎯 步骤4: 创建触发器..." -ForegroundColor Yellow

    $createTriggers = @"
        CREATE TRIGGER realtime_order_trigger
        AFTER INSERT OR UPDATE OR DELETE ON ""Orders""
        FOR EACH ROW EXECUTE FUNCTION trigger_realtime_sync();

        CREATE TRIGGER realtime_notification_trigger
        AFTER INSERT OR UPDATE OR DELETE ON ""Orders""
        FOR EACH ROW EXECUTE FUNCTION notify_realtime_changes();
"@

    $cmd = $connection.CreateCommand()
    $cmd.CommandText = $createTriggers
    $cmd.ExecuteNonQuery()
    Write-Host "  ✅ 创建触发器" -ForegroundColor Green

    # 步骤5: 验证触发器安装
    Write-Host "`n✅ 步骤5: 验证触发器安装..." -ForegroundColor Yellow

    $verifyQuery = @"
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
        ORDER BY tgname;
"@

    $cmd = $connection.CreateCommand()
    $cmd.CommandText = $verifyQuery
    $reader = $cmd.ExecuteReader()

    Write-Host "触发器验证结果:" -ForegroundColor Green
    while ($reader.Read()) {
        $triggerName = $reader.GetString(0)
        $tableName = $reader.GetString(1)
        $enabled = $reader.GetBoolean(2)
        $operations = $reader.GetString(3)
        Write-Host "  ✅ 触发器: $triggerName, 表: $tableName, 启用: $enabled, 支持操作: $operations" -ForegroundColor Gray
    }

    # 步骤6: 测试用户指定的订单
    Write-Host "`n🧪 步骤6: 测试用户指定订单: 4ca86d02-4d8f-4ecd-8641-6bfecf496bd3" -ForegroundColor Yellow
    $orderId = "4ca86d02-4d8f-4ecd-8641-6bfecf496bd3"

    # 检查订单是否存在
    $checkCmd = $connection.CreateCommand()
    $checkCmd.CommandText = "SELECT ""Id"", ""Status"" FROM ""Orders"" WHERE ""Id"" = @id"
    $checkCmd.Parameters.AddWithValue("@id", [System.Guid]::Parse($orderId))

    $orderExists = $false
    $currentStatus = ""
    $reader = $checkCmd.ExecuteReader()
    if ($reader.Read()) {
        $orderExists = $true
        $currentStatus = $reader.GetString(1)
        Write-Host "    📋 当前状态: $currentStatus" -ForegroundColor Gray
    }
    $reader.Close()

    if (-not $orderExists) {
        Write-Host "    📝 订单不存在，创建测试订单..." -ForegroundColor Yellow
        $createCmd = $connection.CreateCommand()
        $createCmd.CommandText = @"
            INSERT INTO ""Orders"" (""Id"", ""Amount"", ""CreatedAt"", ""CustomerName"", ""Status"", ""UpdatedAt"")
            VALUES (@id, @amount, @createdAt, @customerName, @status, @updatedAt)
        "
        $createCmd.Parameters.AddWithValue("@id", [System.Guid]::Parse($orderId))
        $createCmd.Parameters.AddWithValue("@amount", 888.88)
        $createCmd.Parameters.AddWithValue("@createdAt", [DateTime]::UtcNow)
        $createCmd.Parameters.AddWithValue("@customerName", "用户指定测试订单")
        $createCmd.Parameters.AddWithValue("@status", "TRIGGER_FIX_INITIAL")
        $createCmd.Parameters.AddWithValue("@updatedAt", [DateTime]::UtcNow)

        $createCmd.ExecuteNonQuery()
        Write-Host "    ✅ 测试订单已创建" -ForegroundColor Green
    }

    # 清空同步状态表
    $clearCmd = $connection.CreateCommand()
    $clearCmd.CommandText = "DELETE FROM realtime_sync_status"
    $clearCmd.ExecuteNonQuery()

    # 执行UPDATE操作
    $newStatus = "TRIGGER_FIX_SUCCESS_" + (Get-Date -Format "HHmmss")
    Write-Host "    🔄 执行UPDATE: Status -> $newStatus" -ForegroundColor Yellow

    $updateCmd = $connection.CreateCommand()
    $updateCmd.CommandText = @"
        UPDATE ""Orders""
        SET ""Status"" = @status, ""UpdatedAt"" = @updatedAt
        WHERE ""Id"" = @id
    "
    $updateCmd.Parameters.AddWithValue("@id", [System.Guid]::Parse($orderId))
    $updateCmd.Parameters.AddWithValue("@status", $newStatus)
    $updateCmd.Parameters.AddWithValue("@updatedAt", [DateTime]::UtcNow)

    $rowsAffected = $updateCmd.ExecuteNonQuery()
    Write-Host "    ✅ UPDATE完成，影响行数: $rowsAffected" -ForegroundColor Green

    # 等待触发器执行
    Write-Host "    ⏳ 等待触发器执行(5秒)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5

    # 检查同步结果
    $syncCheckCmd = $connection.CreateCommand()
    $syncCheckCmd.CommandText = @"
        SELECT COUNT(*)
        FROM realtime_sync_status
        WHERE last_order_id = @orderId AND sync_type = 'UPDATE'
    "
    $syncCheckCmd.Parameters.AddWithValue("@orderId", [System.Guid]::Parse($orderId))

    $syncCount = [int]$syncCheckCmd.ExecuteScalar()
    Write-Host "    📊 同步记录数: $syncCount" -ForegroundColor Gray

    # 最终验证
    Write-Host "`n🎯 最终验证..." -ForegroundColor Magenta
    if ($syncCount -gt 0) {
        Write-Host "🎉 UPDATE触发器修复成功！订单 $orderId 的UPDATE操作已被正确检测！" -ForegroundColor Green
        Write-Host "   - 触发器正在正常工作" -ForegroundColor Gray
        Write-Host "   - 应用程序现在应该会同步这个订单到本地数据库" -ForegroundColor Gray
    } else {
        Write-Host "⚠️ UPDATE触发器仍存在问题，请检查：" -ForegroundColor Red
        Write-Host "   - 数据库权限设置" -ForegroundColor Gray
        Write-Host "   - 应用程序日志" -ForegroundColor Gray
        Write-Host "   - 触发器是否正确安装" -ForegroundColor Gray
    }

    $connection.Close()

} catch {
    Write-Host "🚨 修复过程中发生错误: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "详细信息: $($_.Exception)" -ForegroundColor DarkGray
}

Write-Host "`n" + "="*60
Write-Host "🏁 UPDATE触发器修复完成" -ForegroundColor Green
Write-Host "="*60