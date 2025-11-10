using DebeziumDemoApp.Models;
using DebeziumDemoApp.Core.Interfaces;

namespace DebeziumDemoApp.Services;

public class KafkaHostedService : BackgroundService
{
    private readonly IKafkaService _kafkaService;
    private readonly ICDCMetricsService _metricsService;
    private readonly ILogger<KafkaHostedService> _logger;

    public KafkaHostedService(
        IKafkaService kafkaService,
        ICDCMetricsService metricsService,
        ILogger<KafkaHostedService> logger)
    {
        _kafkaService = kafkaService;
        _metricsService = metricsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Kafka hosted service...");

        try
        {
            // Start the Kafka service for backward compatibility
            await _kafkaService.StartListeningAsync(stoppingToken);

            _logger.LogInformation("Kafka hosted service started, consumer running in background");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Kafka hosted service");
        }

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("Kafka hosted service stopping...");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Kafka hosted service stopping...");

        try
        {
            // KafkaService doesn't have StopAsync, it's managed by disposal
            if (_kafkaService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Kafka hosted service");
        }

        await base.StopAsync(cancellationToken);
    }
}