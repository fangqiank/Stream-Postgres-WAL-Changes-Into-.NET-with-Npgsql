using DebeziumDemoApp.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace DebeziumDemoApp.Services;

public interface IRabbitMQService
{
    Task StartListeningAsync(CancellationToken cancellationToken);
    event Action<DatabaseChangeNotification>? OnDatabaseChange;
}

public class RabbitMQService : IRabbitMQService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMQService> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly string _queueName = "debezium.events";
    private readonly string _exchangeName = "debezium.exchange";
    private readonly string _routingKey = "debezium.events.key";

    public event Action<DatabaseChangeNotification>? OnDatabaseChange;

    public RabbitMQService(
        IConfiguration configuration,
        ILogger<RabbitMQService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:UserName"] ?? "admin",
                Password = _configuration["RabbitMQ:Password"] ?? "admin",
                VirtualHost = _configuration["RabbitMQ:VirtualHost"] ?? "/"
            };

            _logger.LogInformation("Connecting to RabbitMQ at {HostName}:{Port}", factory.HostName, factory.Port);

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // Declare exchange
            _channel.ExchangeDeclare(
                exchange: _exchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            // Declare queue
            _channel.QueueDeclare(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            // Bind queue to exchange
            _channel.QueueBind(
                queue: _queueName,
                exchange: _exchangeName,
                routingKey: _routingKey);

            _logger.LogInformation("Successfully connected to RabbitMQ and listening for CDC events");

            // Start consuming messages
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (sender, args) =>
            {
                try
                {
                    var body = args.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);

                    _logger.LogDebug("Received CDC message: {Message}", message);

                    // Parse the Debezium CDC message
                    var changeNotification = ParseDebeziumMessage(message);
                    if (changeNotification != null)
                    {
                        OnDatabaseChange?.Invoke(changeNotification);
                        _logger.LogInformation("Processed database change: {Operation} on {Table}",
                            changeNotification.Operation, changeNotification.Table);
                    }

                    _channel.BasicAck(args.DeliveryTag, false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing CDC message");
                    // Negative acknowledge and requeue
                    _channel.BasicNack(args.DeliveryTag, false, true);
                }
            };

            _channel.BasicConsume(_queueName, autoAck: false, consumer: consumer);

            _logger.LogInformation("Started consuming messages from RabbitMQ queue: {QueueName}", _queueName);

            // Keep the service running
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to RabbitMQ or consuming messages");
            throw;
        }
    }

    private DatabaseChangeNotification? ParseDebeziumMessage(string message)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;

            // Handle different Debezium message formats
            if (root.TryGetProperty("payload", out var payloadElement))
            {
                return ParsePayload(payloadElement);
            }
            else if (root.TryGetProperty("after", out var afterElement) ||
                     root.TryGetProperty("before", out _))
            {
                // Direct message format (after transformation)
                return ParseDirectMessage(root);
            }

            _logger.LogWarning("Unknown Debezium message format: {Message}", message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Debezium message: {Message}", message);
            return null;
        }
    }

    private DatabaseChangeNotification? ParsePayload(JsonElement payloadElement)
    {
        var operation = payloadElement.GetProperty("op").GetString() ?? "unknown";
        var table = GetTableNameFromPayload(payloadElement);

        Dictionary<string, object> after = new();
        Dictionary<string, object> before = new();

        if (payloadElement.TryGetProperty("after", out var afterElement) && afterElement.ValueKind != JsonValueKind.Null)
        {
            after = ParseRecordToDictionary(afterElement);
        }

        if (payloadElement.TryGetProperty("before", out var beforeElement) && beforeElement.ValueKind != JsonValueKind.Null)
        {
            before = ParseRecordToDictionary(beforeElement);
        }

        return new DatabaseChangeNotification
        {
            Operation = MapOperation(operation),
            Table = table,
            Schema = "public",
            After = after.Count > 0 ? after : null,
            Before = before.Count > 0 ? before : null,
            Timestamp = DateTime.UtcNow,
            Lsn = payloadElement.TryGetProperty("lsn", out var lsnElement) ?
                   lsnElement.GetInt64() : DateTime.UtcNow.Ticks,
            TransactionId = payloadElement.TryGetProperty("txId", out var txIdElement) ?
                           txIdElement.GetInt64().ToString() : Guid.NewGuid().ToString()
        };
    }

    private DatabaseChangeNotification? ParseDirectMessage(JsonElement root)
    {
        var operation = "INSERT"; // Default for direct messages
        if (root.TryGetProperty("op", out var opElement))
        {
            operation = opElement.GetString() ?? "INSERT";
        }

        var table = GetTableNameFromDirectMessage(root);

        Dictionary<string, object> after = new();
        Dictionary<string, object> before = new();

        if (root.TryGetProperty("after", out var afterElement) && afterElement.ValueKind != JsonValueKind.Null)
        {
            after = ParseRecordToDictionary(afterElement);
        }

        if (root.TryGetProperty("before", out var beforeElement) && beforeElement.ValueKind != JsonValueKind.Null)
        {
            before = ParseRecordToDictionary(beforeElement);
        }

        return new DatabaseChangeNotification
        {
            Operation = MapOperation(operation),
            Table = table,
            Schema = "public",
            After = after.Count > 0 ? after : null,
            Before = before.Count > 0 ? before : null,
            Timestamp = DateTime.UtcNow,
            Lsn = DateTime.UtcNow.Ticks,
            TransactionId = Guid.NewGuid().ToString()
        };
    }

    private static string GetTableNameFromPayload(JsonElement payloadElement)
    {
        if (payloadElement.TryGetProperty("source", out var sourceElement))
        {
            if (sourceElement.TryGetProperty("table", out var tableElement))
            {
                return tableElement.GetString() ?? "unknown";
            }
            if (sourceElement.TryGetProperty("relation", out var relationElement))
            {
                return relationElement.GetString() ?? "unknown";
            }
        }
        return "unknown";
    }

    private static string GetTableNameFromDirectMessage(JsonElement root)
    {
        if (root.TryGetProperty("table", out var tableElement))
        {
            return tableElement.GetString() ?? "unknown";
        }
        return "unknown";
    }

    private static Dictionary<string, object> ParseRecordToDictionary(JsonElement recordElement)
    {
        var dict = new Dictionary<string, object>();

        foreach (var property in recordElement.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? (object)DBNull.Value,
                JsonValueKind.Number => GetNumberValue(property.Value),
                JsonValueKind.True or JsonValueKind.False => property.Value.GetBoolean(),
                JsonValueKind.Null => DBNull.Value,
                _ => property.Value.GetRawText()
            };

            dict[property.Name] = value;
        }

        return dict;
    }

    private static object GetNumberValue(JsonElement numberElement)
    {
        if (numberElement.TryGetInt64(out var longValue))
            return longValue;
        if (numberElement.TryGetDecimal(out var decimalValue))
            return decimalValue;
        if (numberElement.TryGetDouble(out var doubleValue))
            return doubleValue;

        return numberElement.GetRawText();
    }

    private static string MapOperation(string debeziumOp)
    {
        return debeziumOp switch
        {
            "c" => "INSERT",
            "u" => "UPDATE",
            "d" => "DELETE",
            "r" => "READ",  // Snapshot
            "INSERT" => "INSERT",
            "UPDATE" => "UPDATE",
            "DELETE" => "DELETE",
            _ => "UNKNOWN"
        };
    }

    public void Dispose()
    {
        try
        {
            _channel?.Close();
            _connection?.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing RabbitMQ resources");
        }
    }
}