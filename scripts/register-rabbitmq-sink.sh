#!/bin/bash

# Wait for Debezium Connect to be ready
echo "Waiting for Debezium Connect to be ready..."
while ! curl -f http://localhost:8083/connectors; do
    echo "Debezium Connect not ready yet..."
    sleep 2
done

echo "Debezium Connect is ready. Registering RabbitMQ sink connector..."

# Register RabbitMQ sink connector to consume CDC events
curl -X POST -H "Content-Type: application/json" \
     -d '{
        "name": "rabbitmq-sink-connector",
        "config": {
          "connector.class": "io.debezium.connector.rabbitmq.RabbitMQSinkConnector",
          "tasks.max": "1",
          "rabbitmq.connection.host": "rabbitmq",
          "rabbitmq.connection.port": "5672",
          "rabbitmq.connection.username": "admin",
          "rabbitmq.connection.password": "admin",
          "rabbitmq.connection.virtual.host": "/",
          "rabbitmq.queue": "debezium.events",
          "rabbitmq.exchange": "debezium.exchange",
          "rabbitmq.routing.key": "debezium.events.key",
          "rabbitmq.exchange.type": "topic",
          "rabbitmq.exchange.durable": "true",
          "rabbitmq.queue.durable": "true",
          "topics": "debezium.products,debezium.orders,debezium.categories",
          "key.converter": "org.apache.kafka.connect.json.JsonConverter",
          "value.converter": "org.apache.kafka.connect.json.JsonConverter",
          "key.converter.schemas.enable": "false",
          "value.converter.schemas.enable": "false",
          "transforms": "unwrap",
          "transforms.unwrap.type": "io.debezium.transforms.ExtractNewRecordState",
          "transforms.unwrap.drop.tombstones": "false",
          "transforms.unwrap.add.fields": "op,ts_ms,table,db"
        }
      }' \
     http://localhost:8083/connectors

echo ""
echo "RabbitMQ sink connector registration completed."

# Wait a moment and check the connector status
sleep 5
echo "Checking RabbitMQ sink connector status..."
curl -s http://localhost:8083/connectors/rabbitmq-sink-connector/status | jq '.'