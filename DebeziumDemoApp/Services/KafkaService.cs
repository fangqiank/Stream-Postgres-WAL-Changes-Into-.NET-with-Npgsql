using DebeziumDemoApp.Models;
using System.Text.Json;
using Confluent.Kafka;

namespace DebeziumDemoApp.Services;

public interface IKafkaService
{
    Task StartListeningAsync(CancellationToken cancellationToken);
    event Action<DatabaseChangeNotification>? OnDatabaseChange;
}

public class KafkaService : IKafkaService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaService> _logger;
    private IConsumer<Ignore, string>? _consumer;
    private readonly string[] _topics;

    public event Action<DatabaseChangeNotification>? OnDatabaseChange;

    public KafkaService(
        IConfiguration configuration,
        ILogger<KafkaService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Get topics from configuration
        var topicsSection = _configuration.GetSection("Debezium:Topics");
        _topics = topicsSection.Get<string[]>() ?? new[] { "debezium.demo.products", "debezium.demo.orders", "debezium.demo.categories" };
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _configuration["Debezium:BootstrapServers"] ?? "localhost:9092",
                GroupId = _configuration["Debezium:GroupId"] ?? "debezium-demo-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = bool.Parse(_configuration["Debezium:EnableAutoCommit"] ?? "false"),
                AutoCommitIntervalMs = int.Parse(_configuration["Debezium:AutoCommitIntervalMs"] ?? "1000"),
                // Enable auto commit when we successfully process a message
                EnableAutoOffsetStore = false
            };

            _consumer = new ConsumerBuilder<Ignore, string>(config).Build();

            _logger.LogInformation("Connecting to Kafka at {BootstrapServers}", config.BootstrapServers);

            // Subscribe to all Debezium topics
            _consumer.Subscribe(_topics);
            _logger.LogInformation("Subscribed to Kafka topics: {Topics}", string.Join(", ", _topics));

            _logger.LogInformation("Successfully connected to Kafka and listening for CDC events");

            // Start consuming messages
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(cancellationToken);

                    if (consumeResult != null && !string.IsNullOrEmpty(consumeResult.Message.Value))
                    {
                        _logger.LogDebug("Received CDC message from topic {Topic}: {Message}",
                            consumeResult.Topic, consumeResult.Message.Value.Substring(0, Math.Min(200, consumeResult.Message.Value.Length)));

                        // Parse the Debezium CDC message
                        var changeNotification = ParseDebeziumMessage(consumeResult.Message.Value);
                        if (changeNotification != null)
                        {
                            OnDatabaseChange?.Invoke(changeNotification);
                            _logger.LogInformation("Processed database change: {Operation} on {Table}",
                                changeNotification.Operation, changeNotification.Table);
                        }

                        // Manually store the offset after successful processing
                        _consumer.StoreOffset(consumeResult);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming Kafka message: {Error}", ex.Error.Reason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing CDC message");
                    // Continue processing other messages
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Kafka consumer cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Kafka or consuming messages");
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

            _logger.LogWarning("Unknown Debezium message format: {Message}", message.Substring(0, Math.Min(100, message.Length)));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Debezium message: {Message}", message.Substring(0, Math.Min(100, message.Length)));
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
            _consumer?.Close();
            _consumer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Kafka resources");
        }
    }
}