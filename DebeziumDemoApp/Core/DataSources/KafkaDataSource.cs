using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Models;
using Confluent.Kafka;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace DebeziumDemoApp.Core.DataSources;

/// <summary>
/// Kafka数据源实现，用于从Kafka主题获取CDC变更数据
/// </summary>
public class KafkaDataSource : IDataSource
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaDataSource> _logger;
    private IConsumer<Ignore, string>? _consumer;
    private readonly string[] _topics;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listeningTask;
    private bool _isConnected;

    public string Name { get; }
    public DataSourceType Type => DataSourceType.Kafka;
    public bool IsConnected => _isConnected && _consumer != null;

    public event EventHandler<DatabaseChangeEventArgs>? OnChange;

    public KafkaDataSource(
        string name,
        IConfiguration configuration,
        ILogger<KafkaDataSource> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;

        // 从配置获取主题
        var configKey = $"DataSources:{name}:Topics";
        _topics = _configuration.GetSection(configKey).Get<string[]>()
                  ?? new[] { "debezium.demo.products", "debezium.demo.orders", "debezium.demo.categories" };
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isConnected)
            {
                _logger.LogWarning("[KAFKA] Already connected to Kafka");
                return;
            }

            var config = new ConsumerConfig
            {
                BootstrapServers = _configuration[$"DataSources:{Name}:BootstrapServers"] ?? "localhost:9092",
                GroupId = _configuration[$"DataSources:{Name}:GroupId"] ?? $"{Name}-consumer-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = bool.Parse(_configuration[$"DataSources:{Name}:EnableAutoCommit"] ?? "false"),
                AutoCommitIntervalMs = int.Parse(_configuration[$"DataSources:{Name}:AutoCommitIntervalMs"] ?? "1000"),
                EnableAutoOffsetStore = false,
                // 安全设置
                SecurityProtocol = Enum.TryParse<SecurityProtocol>(_configuration[$"DataSources:{Name}:SecurityProtocol"], out var secProto) ? secProto : SecurityProtocol.Plaintext,
                // 可选的SASL认证
                SaslMechanism = Enum.TryParse<SaslMechanism>(_configuration[$"DataSources:{Name}:SaslMechanism"], out var saslMech) ? saslMech : SaslMechanism.Plain,
                SaslUsername = _configuration[$"DataSources:{Name}:SaslUsername"],
                SaslPassword = _configuration[$"DataSources:{Name}:SaslPassword"]
            };

            _consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            _consumer.Subscribe(_topics);

            _cancellationTokenSource = new CancellationTokenSource();
            _listeningTask = StartListeningAsync(_cancellationTokenSource.Token);

            _isConnected = true;
            _logger.LogInformation("[KAFKA] Connected to Kafka, topics: {Topics}", string.Join(", ", _topics));

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KAFKA] Failed to connect to Kafka");
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

            _consumer?.Close();
            _consumer?.Dispose();
            _consumer = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            _isConnected = false;
            _logger.LogInformation("[KAFKA] Disconnected from Kafka");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KAFKA] Error during disconnect");
        }
    }

    public async IAsyncEnumerable<DatabaseChange> GetChangesAsync<T>(
        Func<DatabaseChange, bool>? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Kafka data source is not connected");
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
                _logger.LogError(ex, "[KAFKA] Error in change producer for type {Type}", typeof(T).Name);
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
            throw new InvalidOperationException("Kafka data source is not connected");
        }

        var expectedTopic = $"debezium.demo.{tableName.ToLower()}";

        while (!cancellationToken.IsCancellationRequested)
        {
            var change = await ConsumeWithRetryAsync(expectedTopic, cancellationToken);

            if (change != null && (filter == null || filter(change)))
            {
                yield return change;
            }
        }
    }

    private async Task<DatabaseChange?> ConsumeWithRetryAsync(
        string expectedTopic,
        CancellationToken cancellationToken)
    {
        try
        {
            var consumeResult = _consumer!.Consume(cancellationToken);

            // 检查是否是期望的主题
            if (!consumeResult.Topic.Equals(expectedTopic, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var change = ParseKafkaMessage(consumeResult.Message.Value);

            // 手动提交偏移量
            if (change != null)
            {
                _consumer.Commit(consumeResult);
            }

            return change;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KAFKA] Error consuming message from topic {Topic}", expectedTopic);
            await Task.Delay(1000, cancellationToken); // 等待1秒后重试
            return null;
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
            if (_consumer == null)
            {
                health.IsHealthy = false;
                health.Status = "Disconnected";
                health.Message = "Kafka consumer is not initialized";
                return health;
            }

            // 尝试获取消费者组信息
            var adminConfig = new AdminClientConfig
            {
                BootstrapServers = _configuration[$"DataSources:{Name}:BootstrapServers"] ?? "localhost:9092"
            };

            using var adminClient = new AdminClientBuilder(adminConfig).Build();
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));

            health.IsHealthy = true;
            health.Status = "Connected";
            health.Message = "Kafka connection is healthy";
            health.Metrics.Add("BrokerCount", metadata.Brokers.Count);
            health.Metrics.Add("TopicCount", metadata.Topics.Count);
            health.Metrics.Add("SubscribedTopics", _topics.Length);
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[KAFKA] Health check failed");
        }

        return await Task.FromResult(health);
    }

    private async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[KAFKA] Starting to listen for messages");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer!.Consume(cancellationToken);
                    var change = ParseKafkaMessage(consumeResult.Message.Value);

                    if (change != null)
                    {
                        var eventArgs = new DatabaseChangeEventArgs
                        {
                            Change = change,
                            SourceName = Name
                        };

                        OnChange?.Invoke(this, eventArgs);
                        _logger.LogDebug("[KAFKA] Processed {Operation} on {Table}",
                            change.Operation, change.Table);
                    }

                    // 手动提交偏移量
                    _consumer.Commit(consumeResult);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "[KAFKA] Error consuming message");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[KAFKA] Listening stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KAFKA] Unexpected error in listening task");
        }

        await Task.CompletedTask;
    }

    private DatabaseChange? ParseKafkaMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        try
        {
            // 解析Debezium JSON消息
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
                    ["topic"] = debeziumEvent.Metadata?.ContainsKey("topic") == true
                        ? debeziumEvent.Metadata["topic"]
                        : string.Empty,
                    ["partition"] = debeziumEvent.Metadata?.ContainsKey("partition") == true
                        ? debeziumEvent.Metadata["partition"]
                        : 0,
                    ["offset"] = debeziumEvent.Metadata?.ContainsKey("offset") == true
                        ? debeziumEvent.Metadata["offset"]
                        : 0L
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KAFKA] Failed to parse Kafka message: {Message}", message?.Substring(0, Math.Min(200, message?.Length ?? 0)));
            return null;
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}