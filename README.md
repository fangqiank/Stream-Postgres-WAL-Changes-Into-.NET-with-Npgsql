# Debezium Demo App - .NET 9 Minimal API with Neon PostgreSQL

This project demonstrates how to stream PostgreSQL WAL (Write-Ahead Log) changes into a .NET 9 minimal API application using Neon PostgreSQL and Debezium.

## Features

- **.NET 9 Minimal API** - Modern, lightweight web API
- **Neon PostgreSQL Integration** - Cloud-native PostgreSQL database
- **Debezium CDC** - Change Data Capture for real-time streaming
- **SignalR Hub** - Real-time notifications to connected clients
- **Docker Compose** - Complete development environment setup
- **RESTful APIs** - CRUD operations for products, categories, and orders

## Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   .NET 9 API    │    │    Debezium     │    │  Neon PostgreSQL│
│                 │    │   Connector     │    │                 │
│ - Minimal APIs  │◄──►│                 │◄──►│ - WAL Streaming │
│ - SignalR Hub   │    │ - Kafka Topics  │    │ - CDC Events    │
│ - Real-time     │    │ - Schema Registry│    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Neon PostgreSQL Account](https://neon.tech/)
- [Git](https://git-scm.com/)

## Quick Start

### 1. Clone and Setup

```bash
git clone <repository-url>
cd "Stream Postgres WAL Changes Into .NET with Npgsql App"
cd DebeziumDemoApp
```

### 2. Configure Neon PostgreSQL

Update the connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "NeonPostgres": "Host=your-neon-hostname;Database=neondb;Username=neondb_owner;Password=your-password;SSL Mode=VerifyFull;Channel Binding=Require;"
  }
}
```

### 3. Start Debezium Infrastructure (Optional)

For local development with Debezium:

```bash
cd ..
docker-compose up -d
```

Register the Debezium connector (Windows PowerShell):

```powershell
.\scripts\register-debezium-connector.ps1
```

Or (Bash/WSL):

```bash
chmod +x scripts/register-debezium-connector.sh
./scripts/register-debezium-connector.sh
```

### 4. Run the Application

```bash
cd DebeziumDemoApp
dotnet run
```

The API will be available at `https://localhost:7000` or `http://localhost:5000`.

### 5. Initialize Database

```bash
curl -X POST "https://localhost:7000/api/database/initialize"
```

## API Endpoints

### Health Check
- `GET /health` - Check database connectivity

### Database Management
- `POST /api/database/initialize` - Initialize database schema and sample data

### Products
- `GET /api/products` - List all products
- `POST /api/products` - Create a new product

### Categories
- `GET /api/categories` - List all categories

### Orders
- `GET /api/orders` - List all orders
- `POST /api/orders` - Create a new order

### Real-time Streaming
- `GET /api/changes/stream` - Server-Sent Events stream
- `POST /api/test/changes` - Create test data to demonstrate change detection

### SignalR Hub
- `/hub/databasechanges` - WebSocket endpoint for real-time notifications

## Testing the Application

### 1. Check Health

```bash
curl "https://localhost:7000/health"
```

### 2. Initialize Database

```bash
curl -X POST "https://localhost:7000/api/database/initialize"
```

### 3. Create Data

```bash
# Create a product
curl -X POST "https://localhost:7000/api/products" \
  -H "Content-Type: application/json" \
  -d '{"name":"Test Product","price":29.99,"description":"Test description","stock":100}'

# Create an order
curl -X POST "https://localhost:7000/api/orders" \
  -H "Content-Type: application/json" \
  -d '{"customerId":1,"totalAmount":199.99,"status":"pending"}'
```

### 4. Monitor Changes

#### Option A: Server-Sent Events
```bash
curl "https://localhost:7000/api/changes/stream"
```

#### Option B: SignalR JavaScript Client
```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/7.0.5/signalr.min.js"></script>
<script>
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hub/databasechanges")
    .build();

connection.on("DatabaseChanged", (change) => {
    console.log("Database changed:", change);
});

connection.start().catch(err => console.error(err));
</script>
```

### 5. Test Change Detection

```bash
curl -X POST "https://localhost:7000/api/test/changes"
```

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "NeonPostgres": "Host=your-neon-hostname;Database=neondb;Username=neondb_owner;Password=your-password;SSL Mode=VerifyFull;Channel Binding=Require;"
  },
  "Debezium": {
    "BootstrapServers": "localhost:9092",
    "GroupId": "debezium-demo-group",
    "Topics": ["products", "categories", "orders"],
    "AutoOffsetReset": "earliest",
    "EnableAutoCommit": false
  }
}
```

### Environment Variables

- `NEON_HOST` - Neon PostgreSQL hostname
- `NEON_DATABASE` - Database name
- `NEON_USERNAME` - Database username
- `NEON_PASSWORD` - Database password

## Project Structure

```
DebeziumDemoApp/
├── Services/
│   ├── NeonPostgresService.cs    # PostgreSQL data access
│   └── DebeziumService.cs        # Change data capture service
├── Models/
│   ├── DatabaseModels.cs         # Entity models
│   └── DebeziumModels.cs         # Debezium event models
├── Hubs/
│   └── DatabaseChangeHub.cs      # SignalR hub for real-time updates
├── Program.cs                    # Main application and API endpoints
├── appsettings.json              # Application configuration
├── DebeziumDemoApp.csproj        # Project file
└── Properties/
    └── launchSettings.json       # Development launch settings

scripts/
├── init-postgres.sql             # PostgreSQL initialization script
├── register-debezium-connector.sh # Bash script to register connector
└── register-debezium-connector.ps1 # PowerShell script to register connector

docker-compose.yml                # Debezium infrastructure
README.md                         # This file
```

## How It Works

1. **Database Changes**: When data is modified in Neon PostgreSQL
2. **WAL Streaming**: PostgreSQL streams these changes via Write-Ahead Log
3. **Debezium Capture**: Debezium connector captures these changes
4. **Kafka Topics**: Changes are published to Kafka topics
5. **.NET Service**: Our service consumes these changes
6. **Real-time Updates**: Changes are broadcast via SignalR to connected clients

## Development

### Adding New Entities

1. Add model to `Models/DatabaseModels.cs`
2. Update `Services/DebeziumService.cs` to monitor the new table
3. Add API endpoints in `Program.cs`
4. Update Debezium connector configuration to include the new table

### Local Development with Docker

The `docker-compose.yml` file includes:
- **Zookeeper**: Kafka coordination service
- **Kafka**: Message broker for change events
- **Schema Registry**: Avro schema management
- **Debezium Connect**: CDC connector runtime
- **PostgreSQL**: Local PostgreSQL for testing

### Production Deployment

1. Update connection strings to production Neon PostgreSQL
2. Configure Debezium connector for your Neon database
3. Set up proper SSL certificates for production
4. Configure proper authentication and authorization
5. Set up monitoring and logging

## Troubleshooting

### Common Issues

1. **Connection Timeout**: Check Neon PostgreSQL connection string
2. **SSL Issues**: Verify SSL mode and certificates
3. **Debezium Connector**: Ensure connector is registered and running
4. **Kafka Topics**: Verify topics exist and connector is publishing

### Useful Commands

```bash
# Check Debezium connectors
curl http://localhost:8083/connectors

# Check connector status
curl http://localhost:8083/connectors/postgres-connector/status

# List Kafka topics
docker exec -it debezium-kafka kafka-topics --bootstrap-server localhost:9092 --list

# Consume from Kafka topic
docker exec -it debezium-kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic products --from-beginning
```

## License

This project is for educational purposes to demonstrate Debezium integration with .NET 9 and Neon PostgreSQL.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## Resources

- [.NET 9 Documentation](https://docs.microsoft.com/dotnet/)
- [Neon PostgreSQL](https://neon.tech/)
- [Debezium Documentation](https://debezium.io/documentation/)
- [Apache Kafka](https://kafka.apache.org/)
- [SignalR Documentation](https://docs.microsoft.com/aspnet/core/signalr)