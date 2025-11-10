#!/bin/bash

# Wait for Debezium Connect to be ready
echo "Waiting for Debezium Connect to be ready..."
while ! curl -f http://localhost:8083/connectors; do
    echo "Debezium Connect not ready yet..."
    sleep 2
done

echo "Debezium Connect is ready. Registering PostgreSQL connector..."

# Register the PostgreSQL connector with RabbitMQ sink
curl -X POST -H "Content-Type: application/json" \
     -d '{
        "name": "postgres-connector",
        "config": {
          "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
          "database.hostname": "ep-ancient-moon-a1phzjxn.ap-southeast-1.aws.neon.tech",
          "database.port": "5432",
          "database.user": "neondb_owner",
          "database.password": "npg_CY3hlWj8RBJH",
          "database.dbname": "neondb",
          "database.server.name": "neonserver",
          "plugin.name": "pgoutput",
          "slot.name": "debezium_slot",
          "publication.name": "debezium_publication",
          "table.include.list": "public.products,public.orders,public.categories",
          "key.converter": "org.apache.kafka.connect.json.JsonConverter",
          "value.converter": "org.apache.kafka.connect.json.JsonConverter",
          "key.converter.schemas.enable": "false",
          "value.converter.schemas.enable": "false",
          "transforms": "route",
          "transforms.route.type": "org.apache.kafka.connect.transforms.RegexRouter",
          "transforms.route.regex": "([^.]+)\\.([^.]+)\\.([^.]+)",
          "transforms.route.replacement": "debezium.$3"
        }
      }' \
     http://localhost:8083/connectors

echo ""
echo "Connector registration completed."

# Wait a moment and check the connector status
sleep 5
echo "Checking connector status..."
curl -s http://localhost:8083/connectors/postgres-connector/status | jq '.'