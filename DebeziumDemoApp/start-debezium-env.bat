@echo off
echo Starting Debezium Environment...
echo.

echo 1. Starting PostgreSQL and Kafka services with Docker Compose...
docker-compose up -d

echo.
echo 2. Waiting for services to start...
timeout /t 30 /nobreak

echo.
echo 3. Setting up Debezium connector...
bash -c "./setup-debezium-connector.sh"

echo.
echo 4. Starting the .NET application...
dotnet run --launch-profile https

echo.
echo Debezium environment is running!
echo - PostgreSQL Primary: localhost:5432
echo - PostgreSQL Backup: localhost:5433
echo - Kafka: localhost:9092
echo - RabbitMQ Management: http://localhost:15672 (admin/admin)
echo - Debezium Connect: http://localhost:8083
echo.
pause