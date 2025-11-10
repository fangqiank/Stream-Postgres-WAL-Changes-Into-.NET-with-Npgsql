using DebeziumDemoApp.Models;

namespace DebeziumDemoApp.Services;

public class KafkaHostedService : BackgroundService
{
    private readonly IKafkaService _kafkaService;
    private readonly IRealtimeService _realtimeService;
    private readonly IDataSyncService _dataSyncService;
    private readonly ICDCMetricsService _metricsService;
    private readonly ILogger<KafkaHostedService> _logger;

    public KafkaHostedService(
        IKafkaService kafkaService,
        IRealtimeService realtimeService,
        IDataSyncService dataSyncService,
        ICDCMetricsService metricsService,
        ILogger<KafkaHostedService> logger)
    {
        _kafkaService = kafkaService;
        _realtimeService = realtimeService;
        _dataSyncService = dataSyncService;
        _metricsService = metricsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Kafka hosted service...");

        // Subscribe to database changes, sync to backup, and broadcast via SSE
        _kafkaService.OnDatabaseChange += async (change) =>
        {
            // Record CDC metrics
            _metricsService.RecordChange(change.Operation, change.Table);

            // Sync to backup database first
            await _dataSyncService.SyncChangeToBackupAsync(change);

            // Then broadcast to clients
            await _realtimeService.BroadcastChangeAsync(change);
        };

        // Start Kafka consumer in a background task to avoid blocking the main application
        _ = Task.Run(async () =>
        {
            try
            {
                await _kafkaService.StartListeningAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Kafka consumer task cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Kafka consumer background task");
            }
        }, stoppingToken);

        _logger.LogInformation("Kafka hosted service started, consumer running in background");

        // Keep the hosted service alive but don't block
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Kafka hosted service stopping");
    }
}