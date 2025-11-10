# Docker PostgreSQL Setup for Neon Replication

## Overview

This guide will help you set up a local PostgreSQL instance using Docker to receive replication data from your Neon PostgreSQL database.

## Prerequisites

1. [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
2. At least 4GB of RAM available for Docker
3. Your Neon database connection information

## Quick Start

### 1. Start the PostgreSQL Container

```bash
# Navigate to your project directory
cd "D:\dotnetcore\Stream Postgres WAL Changes Into .NET with Npgsql App"

# Start the local PostgreSQL container
docker-compose up postgres-local -d
```

### 2. Verify Container is Running

```bash
# Check container status
docker-compose ps

# View container logs
docker-compose logs postgres-local
```

### 3. Test Database Connection

```bash
# Connect to the local PostgreSQL database
docker exec -it postgres-local psql -U postgres -d localdb

# Once connected, run these commands to verify setup:
\l                    -- List databases
\dt                   -- List tables
SELECT COUNT(*) FROM orders;  -- Check sample data
\q                    -- Exit psql
```

### 4. Start the Application

```bash
# Make sure you're in the correct directory
cd "Stream Postgres WAL Changes Into .NET with Npgsql App"

# Start the .NET application
dotnet run
```

## Configuration Details

### Docker Compose Configuration

The `docker-compose.yml` has been configured with:

- **Port**: 5432 (standard PostgreSQL port)
- **Database**: `localdb`
- **User**: `postgres`
- **Password**: `localpostgres123`
- **Replication Support**: Logical replication enabled with pgoutput plugin

### Application Configuration

The `appsettings.json` has been updated with:

- **LocalConnection**: Connection string for the Docker PostgreSQL
- **LocalReplication.Enabled**: Set to `true` to enable monitoring

### Database Schema

The container automatically initializes with:

1. **orders table** - Matches your Neon database schema
2. **outbox_events table** - For CDC event tracking
3. **replication_log table** - For monitoring replication status
4. **Indexes and triggers** - For optimal performance
5. **Sample data** - 5 sample orders for testing

## Setting Up Neon-to-Local Replication

### Step 1: Create Publication in Neon

Connect to your Neon database and run:

```sql
-- Create publication for replication
CREATE PUBLICATION cdc_publication
FOR TABLE orders, outbox_events;

-- Verify publication
SELECT * FROM pg_publication;
```

### Step 2: Create Subscription in Local Database

```sql
-- Connect to your local Docker PostgreSQL first
docker exec -it postgres-local psql -U postgres -d localdb

-- Create subscription (replace with your actual Neon connection details)
CREATE SUBSCRIPTION neon_to_local_subscription
CONNECTION 'host=your-neon-hostname user=your-username password=your-password dbname=neondb port=5432 sslmode=require'
PUBLICATION cdc_publication
WITH (slot_name = neon_to_local_slot, create_slot = false, copy_data = true, synchronized_commit = off);
```

### Step 3: Monitor Replication

```sql
-- Check subscription status
SELECT subname, srstate, sractive, srapplydelay
FROM pg_stat_subscription;

-- Monitor recent changes
SELECT * FROM recent_changes
ORDER BY replication_timestamp DESC LIMIT 10;

-- View replication statistics
SELECT * FROM replication_statistics();
```

## Testing the Setup

### 1. Test Application with Local Database

Update your `appsettings.json` to temporarily use the local database:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=localdb;Username=postgres;Password=localpostgres123;SSL Mode=Prefer;Trust Server Certificate=true"
  }
}
```

### 2. Verify CRUD Operations

1. Access the web interface at `https://localhost:7143`
2. Create, read, update, and delete orders
3. Check the changes in the local database:

```sql
SELECT * FROM orders ORDER BY created_at DESC LIMIT 5;
SELECT * FROM outbox_events ORDER BY created_at DESC LIMIT 5;
```

### 3. Test Real-time Monitoring

The application includes monitoring endpoints:

- `GET /health` - Overall health check
- `GET /api/replication/status` - Replication status
- WebSocket `/cdc-ws` - Real-time change notifications

## Troubleshooting

### Common Issues

1. **Port Already in Use**
   ```bash
   # Check what's using port 5432
   netstat -ano | findstr :5432
   # Stop any existing PostgreSQL services
   ```

2. **Container Won't Start**
   ```bash
   # View detailed logs
   docker-compose logs postgres-local
   # Recreate container
   docker-compose down postgres-local
   docker-compose up postgres-local -d
   ```

3. **Connection Refused**
   - Ensure Docker Desktop is running
   - Check if port 5432 is available
   - Verify firewall settings

4. **Replication Issues**
   - Check Neon database permissions
   - Verify network connectivity
   - Review PostgreSQL logs in both databases

### Useful Commands

```bash
# Access PostgreSQL directly
docker exec -it postgres-local psql -U postgres -d localdb

# View container logs in real-time
docker-compose logs -f postgres-local

# Restart the container
docker-compose restart postgres-local

# Stop and remove container (keeps data)
docker-compose down postgres-local

# Stop and remove with data volume
docker-compose down -v postgres-local
```

### Health Checks

The container includes health checks that you can monitor:

```bash
# Check container health
docker ps --format "table {{.Names}}\t{{.Status}}"

# Manual health check
docker exec postgres-local pg_isready -U postgres -d localdb
```

## Next Steps

1. **Configure Neon Connection**: Add your actual Neon database connection details
2. **Set Up Replication**: Follow the replication setup steps above
3. **Test Data Flow**: Verify changes from Neon appear in local database
4. **Monitor Performance**: Use the built-in monitoring endpoints
5. **Scale for Production**: Consider Docker Compose production configurations

## Security Notes

- The password `localpostgres123` is for development only
- In production, use stronger passwords and environment variables
- Consider using Docker secrets for sensitive data
- Enable SSL/TLS for production replication connections

## Performance Optimization

For better performance in production:

1. Increase Docker memory allocation to 8GB+
2. Use dedicated PostgreSQL configuration files
3. Enable connection pooling in the application
4. Monitor WAL file sizes and disk usage
5. Consider using SSD storage for database files