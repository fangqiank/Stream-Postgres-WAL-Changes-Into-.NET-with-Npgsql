using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

namespace DebeziumDemoApp.Core.DataSources;

/// <summary>
/// RabbitMQ数据源实现，用于从RabbitMQ队列获取CDC变更数据
/// </summary>
public class RabbitMQDataSource : IDataSource
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMQDataSource> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly ConcurrentDictionary<string, string> _queueNames;
    private readonly string[] _routingKeys;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listeningTask;
    private bool _isConnected;

    public string Name { get; }
    public DataSourceType Type => DataSourceType.RabbitMQ;
    public bool IsConnected => _isConnected && _connection?.IsOpen == true;

    public event EventHandler<DatabaseChangeEventArgs>? OnChange;

    public RabbitMQDataSource(
        string name,
        IConfiguration configuration,
        ILogger<RabbitMQDataSource> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;

        // 从配置获取路由键和队列名称
        _routingKeys = _configuration.GetSection($"DataSources:{name}:RoutingKeys").Get<string[]>()
                      ?? new[] { "postgres-primary.public.products", "postgres-primary.public.orders", "postgres-primary.public.categories" };

        var queueNamesSection = _configuration.GetSection($"DataSources:{name}:QueueNames");
        _queueNames = new ConcurrentDictionary<string, string>();
        foreach (var queue in queueNamesSection.GetChildren())
        {
            _queueNames.TryAdd(queue.Key, queue.Value!);
        }

        // 设置默认队列名称
        if (_queueNames.IsEmpty)
        {
            _queueNames.TryAdd("products", "products.sync.queue");
            _queueNames.TryAdd("orders", "orders.sync.queue");
            _queueNames.TryAdd("categories", "categories.sync.queue");
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isConnected)
            {
                _logger.LogWarning("[RABBITMQ] Already connected to RabbitMQ");
                return;
            }

            var factory = new ConnectionFactory
            {
                HostName = _configuration[$"DataSources:{Name}:HostName"] ?? "localhost",
                Port = int.Parse(_configuration[$"DataSources:{Name}:Port"] ?? "5672"),
                UserName = _configuration[$"DataSources:{Name}:UserName"] ?? "admin",
                Password = _configuration[$"DataSources:{Name}:Password"] ?? "admin",
                VirtualHost = _configuration[$"DataSources:{Name}:VirtualHost"] ?? "debezium",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            // 设置QoS
            var prefetchCount = int.Parse(_configuration[$"DataSources:{Name}:PrefetchCount"] ?? "10");
            _channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)prefetchCount, global: false);

            // 声明交换机
            var exchangeName = _configuration[$"DataSources:{Name}:ExchangeName"] ?? "debezium.events";
            var exchangeType = _configuration[$"DataSources:{Name}:ExchangeType"] ?? "topic";

            _channel.ExchangeDeclare(exchange: exchangeName, type: exchangeType, durable: true);

            // 为每个路由键创建队列并绑定
            foreach (var routingKey in _routingKeys)
            {
                var tableName = ExtractTableName(routingKey);
                if (_queueNames.TryGetValue(tableName, out var queueName))
                {
                    _channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);
                    _channel.QueueBind(queue: queueName, exchange: exchangeName, routingKey: routingKey);

                    _logger.LogInformation("[RABBITMQ] Bound queue {Queue} to exchange {Exchange} with routing key {RoutingKey}",
                        queueName, exchangeName, routingKey);
                }
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _listeningTask = StartListeningAsync(_cancellationTokenSource.Token);

            _isConnected = true;
            _logger.LogInformation("[RABBITMQ] Connected to RabbitMQ, exchange: {Exchange}", exchangeName);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RABBITMQ] Failed to connect to RabbitMQ");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_isConnected)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();

            if (_listeningTask != null)
            {
                await _listeningTask;
            }

            _channel?.Close();
            _channel?.Dispose();
            _channel = null;

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            _isConnected = false;
            _logger.LogInformation("[RABBITMQ] Disconnected from RabbitMQ");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RABBITMQ] Error during disconnect");
        }
    }

    public async IAsyncEnumerable<DatabaseChange> GetChangesAsync<T>(
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("RabbitMQ data source is not connected");
        }

        var channel = System.Threading.Channels.Channel.CreateUnbounded<DatabaseChange>();

        // 启动生产者任务
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in GetTableChangesAsync(typeof(T).Name, filter, cancellationToken))
                {
                    await channel.Writer.WriteAsync(change, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RABBITMQ] Error in change producer for type {Type}", typeof(T).Name);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // 返回channel中的数据
        await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return change;
        }

        await producerTask;
    }

    public async IAsyncEnumerable<DatabaseChange> GetTableChangesAsync(
        string tableName,
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("RabbitMQ data source is not connected");
        }

        var expectedRoutingKey = $"postgres-primary.public.{tableName.ToLower()}";

        if (!_queueNames.TryGetValue(tableName.ToLower(), out var queueName))
        {
            _logger.LogWarning("[RABBITMQ] No queue configured for table {TableName}", tableName);
            yield break;
        }

        var consumer = new EventingBasicConsumer(_channel);
        var completionSource = new TaskCompletionSource<bool>();

        var receivedChanges = new ConcurrentQueue<DatabaseChange>();

        consumer.Received += (model, ea) =>
        {
            try
            {
                if (!ea.RoutingKey.Equals(expectedRoutingKey, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                var change = ParseRabbitMQMessage(message);

                if (change != null && (filter == null || filter(change)))
                {
                    receivedChanges.Enqueue(change);
                }

                if (!bool.Parse(_configuration[$"DataSources:{Name}:AutoAck"] ?? "false"))
                {
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RABBITMQ] Error processing message from queue {Queue}", queueName);

                if (!bool.Parse(_configuration[$"DataSources:{Name}:AutoAck"] ?? "false"))
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                }
            }
        };

        // 开始消费
        var consumerTag = _channel.BasicConsume(queue: queueName, autoAck: bool.Parse(_configuration[$"DataSources:{Name}:AutoAck"] ?? "false"), consumer: consumer);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (receivedChanges.TryDequeue(out var change))
                {
                    yield return change;
                }
                else
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
        }
        finally
        {
            _channel.BasicCancel(consumerTag);
        }
    }

    public async Task<DataSourceHealth> CheckHealthAsync()
    {
        var health = new DataSourceHealth
        {
            LastCheck = DateTime.UtcNow
        };

        try
        {
            if (_connection == null || !_connection.IsOpen)
            {
                health.IsHealthy = false;
                health.Status = "Disconnected";
                health.Message = "RabbitMQ connection is not established";
                return health;
            }

            if (_channel == null || _channel.IsClosed)
            {
                health.IsHealthy = false;
                health.Status = "Disconnected";
                health.Message = "RabbitMQ channel is not open";
                return health;
            }

            // 尝试获取连接信息
            var connectionName = _connection.ClientProvidedName ?? "Unknown";
            var port = _connection.Endpoint.Port;
            var hostName = _connection.Endpoint.HostName;

            health.IsHealthy = true;
            health.Status = "Connected";
            health.Message = "RabbitMQ connection is healthy";
            health.Metrics.Add("Host", hostName);
            health.Metrics.Add("Port", port);
            health.Metrics.Add("ConnectionName", connectionName);
            health.Metrics.Add("RoutingKeys", _routingKeys.Length);
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[RABBITMQ] Health check failed");
        }

        return await Task.FromResult(health);
    }

    private async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[RABBITMQ] Starting to listen for messages");

        try
        {
            var exchangeName = _configuration[$"DataSources:{Name}:ExchangeName"] ?? "debezium.events";
            var consumer = new EventingBasicConsumer(_channel);

            consumer.Received += (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var change = ParseRabbitMQMessage(message);

                    if (change != null)
                    {
                        var eventArgs = new DatabaseChangeEventArgs
                        {
                            Change = change,
                            SourceName = Name
                        };

                        OnChange?.Invoke(this, eventArgs);
                        _logger.LogDebug("[RABBITMQ] Processed {Operation} on {Table} from routing key {RoutingKey}",
                            change.Operation, change.Table, ea.RoutingKey);
                    }

                    if (!bool.Parse(_configuration[$"DataSources:{Name}:AutoAck"] ?? "false"))
                    {
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RABBITMQ] Error processing message from routing key {RoutingKey}", ea.RoutingKey);

                    if (!bool.Parse(_configuration[$"DataSources:{Name}:AutoAck"] ?? "false"))
                    {
                        _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                }
            };

            // 为每个队列创建消费者
            foreach (var queuePair in _queueNames)
            {
                var consumerTag = _channel.BasicConsume(
                    queue: queuePair.Value,
                    autoAck: bool.Parse(_configuration[$"DataSources:{Name}:AutoAck"] ?? "false"),
                    consumer: consumer);

                _logger.LogInformation("[RABBITMQ] Started consuming from queue {Queue}", queuePair.Value);
            }

            // 等待取消
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[RABBITMQ] Listening stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RABBITMQ] Unexpected error in listening task");
        }

        await Task.CompletedTask;
    }

    private DatabaseChange? ParseRabbitMQMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        try
        {
            // 解析Debezium JSON消息（格式与Kafka相同）
            var debeziumEvent = JsonSerializer.Deserialize<DebeziumChangeEvent>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (debeziumEvent?.Payload == null)
            {
                return null;
            }

            var payload = debeziumEvent.Payload;

            return new DatabaseChange
            {
                Operation = payload.Operation,
                Database = payload.Source.Database,
                Schema = payload.Source.Schema,
                Table = payload.Source.Table,
                Before = payload.Before,
                After = payload.After,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(payload.TimestampMs).DateTime,
                TransactionId = payload.Source.TransactionId,
                Lsn = payload.Source.Lsn,
                Source = new Dictionary<string, object>
                {
                    ["connector"] = payload.Source.Connector,
                    ["version"] = payload.Source.Version,
                    ["name"] = payload.Source.Name,
                    ["snapshot"] = payload.Source.Snapshot
                },
                Metadata = new Dictionary<string, object>
                {
                    ["exchange"] = debeziumEvent.Metadata?.ContainsKey("exchange") == true
                        ? debeziumEvent.Metadata["exchange"]
                        : string.Empty,
                    ["routing.key"] = debeziumEvent.Metadata?.ContainsKey("routing.key") == true
                        ? debeziumEvent.Metadata["routing.key"]
                        : string.Empty,
                    ["message.id"] = debeziumEvent.Metadata?.ContainsKey("message.id") == true
                        ? debeziumEvent.Metadata["message.id"]
                        : string.Empty
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RABBITMQ] Failed to parse RabbitMQ message: {Message}", message?.Substring(0, Math.Min(200, message?.Length ?? 0)));
            return null;
        }
    }

    private string ExtractTableName(string routingKey)
    {
        // 从路由键 "postgres-primary.public.products" 提取表名 "products"
        var parts = routingKey.Split('.');
        return parts.Length > 2 ? parts[2] : routingKey;
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}