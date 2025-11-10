# PowerShell script to start Docker PostgreSQL for Neon replication
# Run this script to set up the local development environment

Write-Host "🐘 Starting Docker PostgreSQL for Neon Replication..." -ForegroundColor Green

# Check if Docker is running
try {
    docker version > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Docker is not running. Please start Docker Desktop first." -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Docker is running" -ForegroundColor Green
} catch {
    Write-Host "❌ Docker is not installed or not running. Please install Docker Desktop first." -ForegroundColor Red
    exit 1
}

# Navigate to project directory
$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectPath
Write-Host "📁 Working directory: $projectPath" -ForegroundColor Yellow

# Stop existing container if running
Write-Host "🛑 Stopping any existing containers..." -ForegroundColor Yellow
docker-compose down postgres-local -v

# Start PostgreSQL container
Write-Host "🚀 Starting PostgreSQL container..." -ForegroundColor Yellow
docker-compose up postgres-local -d

# Wait for PostgreSQL to be ready
Write-Host "⏳ Waiting for PostgreSQL to be ready..." -ForegroundColor Yellow
Write-Host "   (This may take 1-2 minutes for first-time initialization)" -ForegroundColor Gray
$ready = $false
$attempts = 0
$maxAttempts = 90  # Increased from 30 to 90 attempts (3 minutes)

while (-not $ready -and $attempts -lt $maxAttempts) {
    try {
        docker exec postgres-local pg_isready -U postgres -d localdb > $null 2>&1
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            Write-Host "`n✅ PostgreSQL is ready!" -ForegroundColor Green
        } else {
            Start-Sleep -Seconds 2
            $attempts++
            Write-Host "." -NoNewline -ForegroundColor Yellow

            # Show progress every 10 attempts
            if ($attempts % 10 -eq 0) {
                $secondsElapsed = $attempts * 2
                Write-Host " ($secondsElapsed seconds)" -ForegroundColor Gray
            }
        }
    } catch {
        Start-Sleep -Seconds 2
        $attempts++
        Write-Host "." -NoNewline -ForegroundColor Yellow

        # Show progress every 10 attempts
        if ($attempts % 10 -eq 0) {
            $secondsElapsed = $attempts * 2
            Write-Host " ($secondsElapsed seconds)" -ForegroundColor Gray
        }
    }
}

if (-not $ready) {
    Write-Host "`n❌ PostgreSQL failed to start within expected time." -ForegroundColor Red
    Write-Host "📋 Container logs:" -ForegroundColor Yellow
    docker-compose logs postgres-local
    exit 1
}

# Verify database schema
Write-Host "🔍 Verifying database schema..." -ForegroundColor Yellow
try {
    $tableCount = docker exec postgres-local psql -U postgres -d localdb -t -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';"
    $tableCount = $tableCount.Trim()
    Write-Host "✅ Database initialized with $tableCount tables" -ForegroundColor Green

    # Check if sample data exists
    $orderCount = docker exec postgres-local psql -U postgres -d localdb -t -c "SELECT COUNT(*) FROM orders;"
    $orderCount = $orderCount.Trim()
    Write-Host "📊 Sample orders created: $orderCount" -ForegroundColor Green

} catch {
    Write-Host "⚠️  Could not verify database schema, but container is running." -ForegroundColor Yellow
}

# Display connection information
Write-Host "`n🔗 PostgreSQL Connection Information:" -ForegroundColor Cyan
Write-Host "   Host: localhost" -ForegroundColor White
Write-Host "   Port: 5432" -ForegroundColor White
Write-Host "   Database: localdb" -ForegroundColor White
Write-Host "   Username: postgres" -ForegroundColor White
Write-Host "   Password: localpostgres123" -ForegroundColor White
Write-Host "   Connection String: Host=localhost;Port=5432;Database=localdb;Username=postgres;Password=localpostgres123;SSL Mode=Prefer" -ForegroundColor Gray

# Next steps
Write-Host "`n🎯 Next Steps:" -ForegroundColor Cyan
Write-Host "1. Run the .NET application: dotnet run" -ForegroundColor White
Write-Host "2. Access the web interface: https://localhost:7143" -ForegroundColor White
Write-Host "3. Set up Neon-to-local replication (see DOCKER-SETUP.md)" -ForegroundColor White

# Useful commands
Write-Host "`n🛠️  Useful Commands:" -ForegroundColor Cyan
Write-Host "Connect to database: docker exec -it postgres-local psql -U postgres -d localdb" -ForegroundColor Gray
Write-Host "View logs: docker-compose logs -f postgres-local" -ForegroundColor Gray
Write-Host "Stop container: docker-compose down postgres-local" -ForegroundColor Gray

Write-Host "`n✨ Setup complete! Your local PostgreSQL is ready for Neon replication." -ForegroundColor Green