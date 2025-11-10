# Debezium + RabbitMQ CDC Demo with .NET 9 and Neon PostgreSQL

This application demonstrates real-time database change data capture (CDC) using Debezium, RabbitMQ, and .NET 9 with Neon PostgreSQL database.

## Architecture Overview

```
Neon PostgreSQL (Database)
        ↓ (WAL changes)
Debezium PostgreSQL Connector
        ↓ (CDC events)
RabbitMQ (Message Broker)
        ↓ (Messages)
.NET 9 Application
        ↓ (Server-Sent Events)
Web Client (Real-time UI)
```

## Key Features

- **.NET 9 Minimal API** - Modern, lightweight web API
- **Neon PostgreSQL Integration** - Cloud-native PostgreSQL database
- **Debezium CDC** - Real-time change data capture
- **RabbitMQ Messaging** - Reliable message streaming infrastructure
- **Server-Sent Events (SSE)** - Real-time web client communication
- **Docker Compose** - Complete development environment setup
- **RESTful APIs** - CRUD operations for products, categories, and orders

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker](https://www.docker.com/) and Docker Compose
- [Git](https://git-scm.com/)

## Setup Instructions

### 1. Clone and Configure

```bash
git clone <repository-url>
cd "Stream Postgres WAL Changes Into .NET with Npgsql App/DebeziumDemoApp"
```

### 2. Update Configuration

Edit `appsettings.json` and update your Neon PostgreSQL connection string:

```json
{
  "ConnectionStrings": {
    "NeonPostgres": "Host=your-neon-host; Database=neondb; Username=your-username; Password=your-password; SSL Mode=VerifyFull; Channel Binding=Require;"
  }
}
```

### 3. Start Infrastructure Services

Start RabbitMQ and Debezium using Docker Compose:

```bash
cd ..
docker-compose up -d rabbitmq connect
```

This will start:
- RabbitMQ management interface: http://localhost:15672 (admin/admin)
- Debezium Connect API: http://localhost:8083

### 4. Register Debezium Connectors

Wait for the services to be ready, then register the PostgreSQL connector:

```bash
# Make the script executable and run it
chmod +x scripts/register-connector.sh
./scripts/register-connector.sh
```

This script will:
1. Register a PostgreSQL connector to capture changes from your Neon database
2. Configure it to stream changes to RabbitMQ

### 5. Initialize the Database

Start the .NET application and initialize the database:

```bash
cd DebeziumDemoApp
dotnet run

# In another terminal, initialize the database:
curl -X POST http://localhost:5000/api/database/initialize
```

### 6. Run the Application

```bash
dotnet run
```

The application will be available at:
- Web Interface: https://localhost:7056
- API Documentation: https://localhost:7056/openapi
- Health Check: https://localhost:7056/health

## How It Works

### Change Data Capture Flow

1. **Database Changes**: When data is inserted, updated, or deleted in PostgreSQL
2. **WAL Streaming**: PostgreSQL writes changes to the Write-Ahead Log (WAL)
3. **Debezium Connector**: Reads WAL changes and converts them to CDC events
4. **RabbitMQ**: Receives and queues CDC events from Debezium
5. **.NET Application**: Consumes messages from RabbitMQ
6. **Real-time Updates**: Broadcasts changes to connected web clients via SSE

### Real-time Web Updates

The web client uses Server-Sent Events (SSE) instead of WebSockets:
- Connection to `/api/changes/stream` endpoint
- Receives real-time database change events
- Updates the UI dynamically without page refresh
- Automatic reconnection if connection is lost

## Features

- **Real-time Change Detection**: Automatically detects INSERT, UPDATE, DELETE operations
- **Live Dashboard**: Shows products, orders, and categories with live updates
- **Change Log**: Displays detailed history of all database changes
- **Data Management**: Create new products and orders through the web interface
- **Connection Status**: Visual indicator of real-time connection status
- **Automatic Reconnection**: Handles network interruptions gracefully

## API Endpoints

- `GET /api/products` - List all products
- `POST /api/products` - Create a new product
- `GET /api/orders` - List all orders
- `POST /api/orders` - Create a new order
- `GET /api/categories` - List all categories
- `GET /api/changes/stream` - SSE endpoint for real-time updates
- `POST /api/test/changes` - Create test database change
- `GET /health` - Application health check

## Testing the CDC Pipeline

1. **Web Interface**: Use the web interface at https://localhost:7056
   - Add products and orders using the forms
   - Watch the change log update in real-time

2. **Direct Database Changes**: Connect directly to PostgreSQL and make changes
   - Changes will appear in the web interface automatically

3. **API Testing**: Use curl or Postman to test endpoints:
   ```bash
   # Create a test product
   curl -X POST https://localhost:7056/api/products \
     -H "Content-Type: application/json" \
     -d '{"name":"Test Product","price":29.99,"description":"CDC test","stock":50}'
   ```

## Monitoring

### RabbitMQ Management
- URL: http://localhost:15672
- Username: admin
- Password: admin
- Monitor queues, exchanges, and message flow

### Debezium Connect
- URL: http://localhost:8083
- View connector status and configuration
- API endpoints for managing connectors

## Troubleshooting

### Common Issues

1. **RabbitMQ Connection Failed**
   - Ensure Docker is running
   - Check if RabbitMQ container is healthy: `docker ps`
   - Verify ports 5672 and 15672 are available

2. **Debezium Connector Not Receiving Changes**
   - Check PostgreSQL logical replication is enabled
   - Verify connector configuration in Debezium Connect UI
   - Check network connectivity between containers

3. **Database Connection Issues**
   - Verify Neon PostgreSQL connection string
   - Check if database tables exist
   - Ensure SSL/TLS settings are correct

4. **Real-time Updates Not Working**
   - Check SSE connection in browser developer tools
   - Verify RabbitMQ messages are being consumed
   - Check application logs for errors

### Logs and Debugging

- **Application Logs**: Check console output for .NET application
- **Docker Logs**: `docker-compose logs <service-name>`
- **RabbitMQ Logs**: Available in RabbitMQ management interface
- **Debezium Logs**: Check connector status and error messages

## Architecture Benefits

- **Scalability**: RabbitMQ can handle multiple consumers and high throughput
- **Reliability**: Message queuing ensures no changes are lost
- **Decoupling**: Database and application are loosely coupled
- **Real-time**: Immediate notification of changes
- **Flexibility**: Can add multiple consumers for different purposes

## Production Considerations

- **Security**: Use SSL/TLS for all connections
- **Authentication**: Implement proper authentication for RabbitMQ
- **Monitoring**: Add comprehensive logging and monitoring
- **Backup**: Regular database backups and point-in-time recovery
- **Performance**: Optimize Debezium connector settings for your workload
- **High Availability**: Deploy RabbitMQ in clustered mode

## Next Steps

- Add authentication and authorization
- Implement data transformation and enrichment
- Add more sophisticated error handling
- Deploy to production environment
- Add monitoring and alerting
- Implement data retention policies

## Contributing

Feel free to submit issues and pull requests to improve this CDC demo application.