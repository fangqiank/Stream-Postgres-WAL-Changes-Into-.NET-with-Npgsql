# PowerShell script to register Debezium PostgreSQL connector

Write-Host "Waiting for Debezium Connect to start..." -ForegroundColor Yellow

# Wait for Debezium Connect to be ready
do {
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:8083/connectors" -Method Get -ErrorAction Stop
        Write-Host "Debezium Connect is ready." -ForegroundColor Green
        break
    }
    catch {
        Write-Host "Debezium Connect not ready, waiting..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
    }
} while ($true)

Write-Host "Registering PostgreSQL connector..." -ForegroundColor Cyan

# Connector configuration
$connectorConfig = @{
    name = "postgres-connector"
    config = @{
        "connector.class" = "io.debezium.connector.postgresql.PostgresConnector"
        "database.hostname" = "postgres"
        "database.port" = "5432"
        "database.user" = "debezium_user"
        "database.password" = "debezium_password"
        "database.dbname" = "demo"
        "database.server.name" = "postgres-demo"
        "plugin.name" = "pgoutput"
        "slot.name" = "debezium_slot"
        "publication.name" = "debezium_publication"
        "table.include.list" = "demo.products,demo.categories,demo.orders"
        "transforms" = "route"
        "transforms.route.type" = "org.apache.kafka.connect.transforms.RegexRouter"
        "transforms.route.regex" = "([^.]+)\.([^.]+)\.([^.]+)"
        "transforms.route.replacement" = "`$3"
    }
}

$jsonConfig = $connectorConfig | ConvertTo-Json -Depth 10

try {
    $response = Invoke-RestMethod -Uri "http://localhost:8083/connectors/" -Method Post -Body $jsonConfig -ContentType "application/json" -Headers @{"Accept"="application/json"}
    Write-Host "Connector registered successfully!" -ForegroundColor Green
    Write-Host "Connector details:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 10
}
catch {
    Write-Host "Failed to register connector: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`nCheck connector status with:" -ForegroundColor Yellow
Write-Host "curl http://localhost:8083/connectors/postgres-connector/status" -ForegroundColor Gray
Write-Host "or PowerShell:" -ForegroundColor Gray
Write-Host "Invoke-RestMethod -Uri 'http://localhost:8083/connectors/postgres-connector/status' | ConvertTo-Json -Depth 10" -ForegroundColor Gray