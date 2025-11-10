#!/bin/bash

# Wait for Debezium Connect to be ready
echo "Waiting for Debezium Connect to start..."
until curl -s http://localhost:8083/ | grep -q "Debezium"; do
    sleep 2
done
echo "Debezium Connect is ready!"

# Configure PostgreSQL connector
echo "Registering PostgreSQL connector..."
curl -i -X POST -H "Accept:application/json" -H "Content-Type:application/json" \
  localhost:8083/connectors/ \
  -d '{
    "name": "postgres-connector",
    "config": {
      "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
      "database.hostname": "postgres-primary",
      "database.port": "5432",
      "database.user": "postgres",
      "database.password": "postgres",
      "database.dbname": "demo",
      "database.server.name": "postgres-primary",
      "plugin.name": "pgoutput",
      "slot.name": "debezium_slot",
      "publication.name": "debezium_publication",
      "table.include.list": "public.products,public.orders,public.categories",
      "transforms": "route",
      "transforms.route.type": "org.apache.kafka.connect.transforms.RegexRouter",
      "transforms.route.regex": "([^.]+)\\.([^.]+)\\.([^.]+)",
      "transforms.route.replacement": "debezium.$3"
    }
  }'

echo ""
echo "Debezium connector configuration complete!"
echo "Check connector status at: http://localhost:8083/connectors/postgres-connector/status"