# PowerShell script to test the synchronization of user's specific order
Write-Host "🧪 Testing order synchronization for 019a62dd-0d37-7622-9604-4fb2f710f403" -ForegroundColor Green

$orderId = "019a62dd-0d37-7622-9604-4fb2f710f403"

# Connection strings
$neonConnection = "Host=ep-rapid-wind-a5cne0p3-pooler.us-east-2.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_l1xO8KZ3vNa6;SSL Mode=Require;Trust Server Certificate=True;"
$localConnection = "Host=localhost;Port=5432;Database=localdb;Username=postgres;Password=myPassword;SSL Mode=Prefer;Trust Server Certificate=True;"

try {
    # Install Npgsql PowerShell module if needed
    if (-not (Get-Module -ListAvailable -Name Npgsql)) {
        Write-Host "📦 Installing Npgsql module..." -ForegroundColor Yellow
        Install-Package -Name Npgsql -Scope CurrentUser -Force
    }

    # Import Npgsql
    Add-Type -Path "C:\Users\fangq\.nuget\packages\npgsql\8.0.3\lib\netstandard2.0\Npgsql.dll"

    # 1. Check if order exists in Neon database
    Write-Host "`n📡 Checking Neon database..." -ForegroundColor Cyan
    $neonOrder = $null
    try {
        $neonConn = New-Object Npgsql.NpgsqlConnection($neonConnection)
        $neonConn.Open()

        $neonCmd = $neonConn.CreateCommand()
        $neonCmd.CommandText = "SELECT ""Id"", ""Amount"", ""CreatedAt"", ""CustomerName"", ""Status"", ""UpdatedAt"" FROM ""Orders"" WHERE ""Id"" = @orderId"
        $neonCmd.Parameters.AddWithValue("@orderId", [System.Guid]::Parse($orderId))

        $neonReader = $neonCmd.ExecuteReader()
        if ($neonReader.Read()) {
            $neonOrder = @{
                Id = $neonReader.GetGuid(0)
                Amount = $neonReader.GetDecimal(1)
                CreatedAt = $neonReader.GetDateTime(2)
                CustomerName = $neonReader.GetString(3)
                Status = $neonReader.GetString(4)
                UpdatedAt = $neonReader.GetDateTime(5)
            }
            Write-Host "✅ Found in Neon: ID=$($neonOrder.Id), Status=$($neonOrder.Status), Customer=$($neonOrder.CustomerName), Amount=$($neonOrder.Amount), Updated=$($neonOrder.UpdatedAt)" -ForegroundColor Green
        } else {
            Write-Host "❌ Order NOT found in Neon database" -ForegroundColor Red
            exit
        }
        $neonConn.Close()
    } catch {
        Write-Host "❌ Error accessing Neon: $($_.Exception.Message)" -ForegroundColor Red
        exit
    }

    # 2. Check if order exists in local database
    Write-Host "`n📋 Checking local database..." -ForegroundColor Cyan
    $localOrder = $null
    try {
        $localConn = New-Object Npgsql.NpgsqlConnection($localConnection)
        $localConn.Open()

        $localCmd = $localConn.CreateCommand()
        $localCmd.CommandText = "SELECT id, amount, created_at, customername, status, updated_at FROM orders WHERE id = @orderId"
        $localCmd.Parameters.AddWithValue("@orderId", [System.Guid]::Parse($orderId))

        $localReader = $localCmd.ExecuteReader()
        if ($localReader.Read()) {
            $localOrder = @{
                Id = $localReader.GetGuid(0)
                Amount = $localReader.GetDecimal(1)
                CreatedAt = $localReader.GetDateTime(2)
                CustomerName = $localReader.GetString(3)
                Status = $localReader.GetString(4)
                UpdatedAt = $localReader.GetDateTime(5)
            }
            Write-Host "✅ Found in Local: ID=$($localOrder.Id), Status=$($localOrder.Status), Customer=$($localOrder.CustomerName), Amount=$($localOrder.Amount), Updated=$($localOrder.UpdatedAt)" -ForegroundColor Green
        } else {
            Write-Host "❌ Order NOT found in local database" -ForegroundColor Red
        }
        $localConn.Close()
    } catch {
        Write-Host "❌ Error accessing Local: $($_.Exception.Message)" -ForegroundColor Red
    }

    # 3. Check realtime_sync_status table
    Write-Host "`n📊 Checking realtime_sync_status table..." -ForegroundColor Cyan
    try {
        $neonConn = New-Object Npgsql.NpgsqlConnection($neonConnection)
        $neonConn.Open()

        $statusCmd = $neonConn.CreateCommand()
        $statusCmd.CommandText = "SELECT id, last_sync_time, last_order_id, sync_type, is_active, created_at FROM realtime_sync_status ORDER BY created_at DESC LIMIT 5"

        $statusReader = $statusCmd.ExecuteReader()
        Write-Host "Recent realtime_sync_status records:" -ForegroundColor Yellow
        while ($statusReader.Read()) {
            $orderIdStr = if ($statusReader.IsDBNull(2)) { "NULL" } else { $statusReader.GetGuid(2).ToString() }
            Write-Host "  ID=$($statusReader.GetInt32(0)), Time=$($statusReader.GetDateTime(1)), OrderID=$orderIdStr, Type=$($statusReader.GetString(3)), Active=$($statusReader.GetBoolean(4))" -ForegroundColor Gray
        }
        $neonConn.Close()
    } catch {
        Write-Host "❌ Error checking sync status: $($_.Exception.Message)" -ForegroundColor Red
    }

    # 4. Manually insert trigger record to test sync
    Write-Host "`n🎯 Manually triggering sync..." -ForegroundColor Cyan
    try {
        $neonConn = New-Object Npgsql.NpgsqlConnection($neonConnection)
        $neonConn.Open()

        $triggerCmd = $neonConn.CreateCommand()
        $triggerCmd.CommandText = "INSERT INTO realtime_sync_status (last_order_id, sync_type, is_active) VALUES (@orderId, 'UPDATE', true)"
        $triggerCmd.Parameters.AddWithValue("@orderId", [System.Guid]::Parse($orderId))

        $rows = $triggerCmd.ExecuteNonQuery()
        Write-Host "✅ Manual trigger inserted: $rows row(s) affected" -ForegroundColor Green
        $neonConn.Close()
    } catch {
        Write-Host "❌ Error triggering manual sync: $($_.Exception.Message)" -ForegroundColor Red
    }

    # 5. Wait and check local database again
    Write-Host "`n⏳ Waiting 3 seconds for sync..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3

    Write-Host "`n🔄 Checking local database again..." -ForegroundColor Cyan
    try {
        $localConn = New-Object Npgsql.NpgsqlConnection($localConnection)
        $localConn.Open()

        $checkCmd = $localConn.CreateCommand()
        $checkCmd.CommandText = "SELECT id, amount, created_at, customername, status, updated_at FROM orders WHERE id = @orderId"
        $checkCmd.Parameters.AddWithValue("@orderId", [System.Guid]::Parse($orderId))

        $checkReader = $checkCmd.ExecuteReader()
        if ($checkReader.Read()) {
            $updatedLocalOrder = @{
                Id = $checkReader.GetGuid(0)
                Amount = $checkReader.GetDecimal(1)
                CreatedAt = $checkReader.GetDateTime(2)
                CustomerName = $checkReader.GetString(3)
                Status = $checkReader.GetString(4)
                UpdatedAt = $checkReader.GetDateTime(5)
            }
            Write-Host "✅ SYNC SUCCESS! Local: Status=$($updatedLocalOrder.Status), Customer=$($updatedLocalOrder.CustomerName), Amount=$($updatedLocalOrder.Amount), Updated=$($updatedLocalOrder.UpdatedAt)" -ForegroundColor Green

            # Compare with Neon order
            if ($neonOrder -and $updatedLocalOrder) {
                if ($neonOrder.Status -eq $updatedLocalOrder.Status -and $neonOrder.CustomerName -eq $updatedLocalOrder.CustomerName -and $neonOrder.Amount -eq $updatedLocalOrder.Amount) {
                    Write-Host "🎉 PERFECT SYNC! Data matches between Neon and Local databases!" -ForegroundColor Green
                } else {
                    Write-Host "⚠️  PARTIAL SYNC! Data differs between Neon and Local databases" -ForegroundColor Yellow
                    Write-Host "   Neon: Status=$($neonOrder.Status), Local: Status=$($updatedLocalOrder.Status)" -ForegroundColor Yellow
                }
            }
        } else {
            Write-Host "❌ Still not found in local database" -ForegroundColor Red
        }
        $localConn.Close()
    } catch {
        Write-Host "❌ Error checking final local state: $($_.Exception.Message)" -ForegroundColor Red
    }

} catch {
    Write-Host "🚨 Fatal error: $($_.Exception.Message)" -ForegroundColor Red
}