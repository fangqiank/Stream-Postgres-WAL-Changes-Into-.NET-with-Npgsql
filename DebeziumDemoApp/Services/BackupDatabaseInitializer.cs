namespace DebeziumDemoApp.Services;

public class BackupDatabaseInitializer : IHostedService
{
    private readonly IBackupPostgresService _backupDb;
    private readonly ILogger<BackupDatabaseInitializer> _logger;

    public BackupDatabaseInitializer(
        IBackupPostgresService backupDb,
        ILogger<BackupDatabaseInitializer> logger)
    {
        _backupDb = backupDb;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[BACKUP INIT] Initializing backup database...");

        try
        {
            // Test backup database connection
            var connectionTest = await _backupDb.TestConnectionAsync();
            if (!connectionTest)
            {
                _logger.LogError("[BACKUP INIT] Failed to connect to backup database");
                return;
            }

            _logger.LogInformation("[BACKUP DB] Backup database connection successful");

            // Initialize backup database schema
            await _backupDb.InitializeDatabaseAsync();

            _logger.LogInformation("[BACKUP DB] Backup database initialized successfully");
            _logger.LogInformation("[BACKUP INIT] Backup database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BACKUP INIT] Error during backup database initialization");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[BACKUP INIT] Backup database initializer stopped");
        return Task.CompletedTask;
    }
}