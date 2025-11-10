namespace DebeziumDemoApp.Services;

public class RabbitMQHostedService : BackgroundService
{
    private readonly IRabbitMQService _rabbitMQService;
    private readonly ILogger<RabbitMQHostedService> _logger;

    public RabbitMQHostedService(
        IRabbitMQService rabbitMQService,
        ILogger<RabbitMQHostedService> logger)
    {
        _rabbitMQService = rabbitMQService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting RabbitMQ hosted service...");

        try
        {
            await _rabbitMQService.StartListeningAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RabbitMQ hosted service");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping RabbitMQ hosted service...");

        if (_rabbitMQService is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await base.StopAsync(cancellationToken);
    }
}