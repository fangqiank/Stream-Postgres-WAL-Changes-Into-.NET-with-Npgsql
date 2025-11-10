# PowerShell script to test UPDATE operation for specific user order
Write-Host "🎯 测试用户指定订单的UPDATE操作: 019a62dd-0d37-7622-9604-4fb2f710f403" -ForegroundColor Green

$orderId = "019a62dd-0d37-7622-9604-4fb2f710f403"
$neonConnection = "Host=ep-rapid-wind-a5cne0p3-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_l1xO8KZ3vNa6;SSL Mode=Require;Trust Server Certificate=True;"

try {
    # Try to use Npgsql.dll from existing packages
    $npgsqlPath = "D:\dotnetcore\Stream Postgres WAL Changes Into .NET with Npgsql App\Stream Postgres WAL Changes Into .NET with Npgsql App\bin\Debug\net9.0"
    if (Test-Path "$npgsqlPath\Npgsql.dll") {
        Add-Type -Path "$npgsqlPath\Npgsql.dll"
        Write-Host "✅ 已加载Npgsql.dll" -ForegroundColor Green
    } else {
        Write-Host "❌ 找不到Npgsql.dll，尝试全局安装..." -ForegroundColor Yellow
        try {
            Install-Package -Name Npgsql -Scope CurrentUser -Force -ErrorAction SilentlyContinue
            Add-Type -Path (Get-Package Npgsql).Source + "\lib\netstandard2.0\Npgsql.dll"
        } catch {
            Write-Host "❌ 无法加载Npgsql，使用基础测试" -ForegroundColor Red
            Test-BasicUpdateOnly
            return
        }
    }

    Write-Host "`n📡 连接到Neon数据库..." -ForegroundColor Cyan
    $neonConn = New-Object Npgsql.NpgsqlConnection($neonConnection)
    $neonConn.Open()

    # 1. Check if order exists
    Write-Host "`n🔍 检查订单是否存在..." -ForegroundColor Cyan
    $checkCmd = $neonConn.CreateCommand()
    $checkCmd.CommandText = "SELECT ""Id"", ""Status"", ""CustomerName"", ""UpdatedAt"" FROM ""Orders"" WHERE ""Id"" = @orderId"
    $checkCmd.Parameters.AddWithValue("@orderId", [System.Guid]::Parse($orderId))

    $reader = $checkCmd.ExecuteReader()
    $orderExists = $false
    $currentStatus = ""
    $currentCustomer = ""
    $currentUpdatedAt = [DateTime]::MinValue

    if ($reader.Read()) {
        $orderExists = $true
        $currentStatus = $reader.GetString(1)
        $currentCustomer = $reader.GetString(2)
        $currentUpdatedAt = $reader.GetDateTime(3)
        Write-Host "✅ 找到订单: Status=$currentStatus, Customer=$currentCustomer, UpdatedAt=$currentUpdatedAt" -ForegroundColor Green
    } else {
        Write-Host "❌ 订单不存在，创建测试订单..." -ForegroundColor Yellow
    }
    $reader.Close()

    # 2. Create order if it doesn't exist
    if (-not $orderExists) {
        $createCmd = $neonConn.CreateCommand()
        $createCmd.CommandText = @"
            INSERT INTO ""Orders"" (""Id"", ""Amount"", ""CreatedAt"", ""CustomerName"", ""Status"", ""UpdatedAt"")
            VALUES (@id, @amount, @createdAt, @customerName, @status, @updatedAt)
"@
        $createCmd.Parameters.AddWithValue("@id", [System.Guid]::Parse($orderId))
        $createCmd.Parameters.AddWithValue("@amount", 299.99)
        $createCmd.Parameters.AddWithValue("@createdAt", [DateTime]::UtcNow)
        $createCmd.Parameters.AddWithValue("@customerName", "用户指定测试订单")
        $createCmd.Parameters.AddWithValue("@status", "test_initial")
        $createCmd.Parameters.AddWithValue("@updatedAt", [DateTime]::UtcNow)

        $createCmd.ExecuteNonQuery()
        Write-Host "✅ 测试订单已创建" -ForegroundColor Green
        $currentStatus = "test_initial"
        $currentCustomer = "用户指定测试订单"
    }

    # 3. Clear realtime_sync_status table
    Write-Host "`n🧹 清空realtime_sync_status表..." -ForegroundColor Cyan
    $clearCmd = $neonConn.CreateCommand()
    $clearCmd.CommandText = "DELETE FROM realtime_sync_status"
    $clearCmd.ExecuteNonQuery()

    # 4. Perform UPDATE operation
    $newStatus = "USER_TEST_$([DateTime]::UtcNow.ToString('HHmmss'))"
    $newCustomer = "用户测试客户_$([DateTime]::UtcNow.ToString('HHmmss'))"

    Write-Host "`n🔄 执行UPDATE操作..." -ForegroundColor Cyan
    Write-Host "   状态: $currentStatus -> $newStatus" -ForegroundColor Gray
    Write-Host "   客户: $currentCustomer -> $newCustomer" -ForegroundColor Gray

    $updateCmd = $neonConn.CreateCommand()
    $updateCmd.CommandText = @"
        UPDATE ""Orders""
        SET ""Status"" = @status, ""CustomerName"" = @customerName, ""UpdatedAt"" = @updatedAt
        WHERE ""Id"" = @id
"@
    $updateCmd.Parameters.AddWithValue("@id", [System.Guid]::Parse($orderId))
    $updateCmd.Parameters.AddWithValue("@status", $newStatus)
    $updateCmd.Parameters.AddWithValue("@customerName", $newCustomer)
    $updateCmd.Parameters.AddWithValue("@updatedAt", [DateTime]::UtcNow)

    $rowsAffected = $updateCmd.ExecuteNonQuery()
    Write-Host "✅ UPDATE完成，影响行数: $rowsAffected" -ForegroundColor Green

    # 5. Wait for trigger
    Write-Host "`n⏳ 等待触发器执行(5秒)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5

    # 6. Check realtime_sync_status
    Write-Host "`n📊 检查realtime_sync_status表..." -ForegroundColor Cyan
    $statusCmd = $neonConn.CreateCommand()
    $statusCmd.CommandText = @"
        SELECT id, last_order_id, sync_type, last_sync_time
        FROM realtime_sync_status
        ORDER BY last_sync_time DESC
        LIMIT 5
"@

    $statusReader = $statusCmd.ExecuteReader()
    $updateFound = $false

    Write-Host "最近的同步记录:" -ForegroundColor Yellow
    while ($statusReader.Read()) {
        $recordId = $statusReader.GetInt32(0)
        $recordOrderId = if ($statusReader.IsDBNull(1)) { "NULL" } else { $statusReader.GetGuid(1).ToString() }
        $recordType = $statusReader.GetString(2)
        $recordTime = $statusReader.GetDateTime(3)

        Write-Host "  ID=$recordId, OrderId=$recordOrderId, Type=$recordType, Time=$recordTime" -ForegroundColor Gray

        if ($recordOrderId -eq $orderId -and $recordType -eq "UPDATE") {
            $updateFound = $true
        }
    }
    $statusReader.Close()

    # 7. Verify order status
    Write-Host "`n🔍 验证订单更新后的状态..." -ForegroundColor Cyan
    $verifyCmd = $neonConn.CreateCommand()
    $verifyCmd.CommandText = "SELECT ""Status"", ""CustomerName"", ""UpdatedAt"" FROM ""Orders"" WHERE ""Id"" = @orderId"
    $verifyCmd.Parameters.AddWithValue("@orderId", [System.Guid]::Parse($orderId))

    $verifyReader = $verifyCmd.ExecuteReader()
    if ($verifyReader.Read()) {
        $finalStatus = $verifyReader.GetString(0)
        $finalCustomer = $verifyReader.GetString(1)
        $finalUpdatedAt = $verifyReader.GetDateTime(2)
        Write-Host "✅ 最终状态: Status=$finalStatus, Customer=$finalCustomer, UpdatedAt=$finalUpdatedAt" -ForegroundColor Green
    }
    $verifyReader.Close()

    # 8. Results
    Write-Host "`n🎯 测试结果:" -ForegroundColor Magenta
    Write-Host "  订单ID: $orderId" -ForegroundColor White
    Write-Host "  UPDATE触发: $(if ($updateFound) { '✅ 成功' } else { '❌ 失败' })" -ForegroundColor $(if ($updateFound) { 'Green' } else { 'Red' })
    Write-Host "  数据库更新: ✅ 成功 ($rowsAffected 行)" -ForegroundColor Green

    if ($updateFound) {
        Write-Host "`n🎉 订单 $orderId 的UPDATE同步测试成功!" -ForegroundColor Green
        Write-Host "   - 触发器正确检测到UPDATE操作" -ForegroundColor Gray
        Write-Host "   - 数据已写入realtime_sync_status表" -ForegroundColor Gray
        Write-Host "   - 应用程序应该会在500ms内处理同步" -ForegroundColor Gray
    } else {
        Write-Host "`n❌ 订单 $orderId 的UPDATE同步测试失败!" -ForegroundColor Red
        Write-Host "   - 触发器未检测到UPDATE操作" -ForegroundColor Gray
        Write-Host "   - realtime_sync_status表中没有UPDATE记录" -ForegroundColor Gray
        Write-Host "   - 可能是触发器权限或配置问题" -ForegroundColor Gray
    }

    $neonConn.Close()

} catch {
    Write-Host "🚨 测试过程中发生错误: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "详细信息: $($_.Exception)" -ForegroundColor Gray
}

function Test-BasicUpdateOnly {
    Write-Host "🔄 执行基础UPDATE测试..." -ForegroundColor Cyan
    # 这里可以添加不依赖Npgsql的基础测试逻辑
    Write-Host "基础测试需要Npgsql连接，跳过..." -ForegroundColor Yellow
}

Write-Host "`n" + ("=" * 60)
Write-Host "🏁 测试完成" -ForegroundColor Green