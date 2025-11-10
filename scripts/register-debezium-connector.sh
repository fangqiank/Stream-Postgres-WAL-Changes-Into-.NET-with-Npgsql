#!/bin/bash

# Wait for Debezium Connect to be ready
echo "Waiting for Debezium Connect to start..."
until curl -f http://localhost:8083/connectors 2>/dev/null; do
    echo "Debezium Connect not ready, waiting..."
    sleep 5
done

echo "Debezium Connect is ready. Registering PostgreSQL connector..."

# Register the PostgreSQL connector
curl -i -X POST -H "Accept:application/json" -H "Content-Type:application/json" \
localhost:8083/connectors/ -d '
{
    "name": "postgres-connector",
    "config": {
        "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
        "database.hostname": "postgres",
        "database.port": "5432",
        "database.user": "debezium_user",
        "database.password": "debezium_password",
        "database.dbname": "demo",
        "database.server.name": "postgres-demo",
        "plugin.name": "pgoutput",
        "slot.name": "debezium_slot",
        "publication.name": "debezium_publication",
        "table.include.list": "demo.products,demo.categories,demo.orders",
        "transforms": "route",
        "transforms.route.type": "org.apache.kafka.connect.transforms.RegexRouter",
        "transforms.route.regex": "([^.]+)\\.([^.]+)\\.([^.]+)",
        "transforms.route.replacement": "$3"
    }
}'

echo -e "\nConnector registration completed."
echo "Check connector status with: curl http://localhost:8083/connectors/postgres-connector/status"