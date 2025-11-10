namespace DebeziumDemoApp.Services;

public class BackupDatabaseInitializer : IHostedService
{
    private readonly IBackupPostgresService _backupDb;
    private readonly IDataSyncService _dataSyncService;
    private readonly ILogger<BackupDatabaseInitializer> _logger;

    public BackupDatabaseInitializer(
        IBackupPostgresService backupDb,
        IDataSyncService dataSyncService,
        ILogger<BackupDatabaseInitializer> logger)
    {
        _backupDb = backupDb;
        _dataSyncService = dataSyncService;
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

            _logger.LogInformation("[BACKUP INIT] Backup database connection successful");

            // Initialize backup database schema
            await _backupDb.InitializeDatabaseAsync();
            _logger.LogInformation("[BACKUP INIT] Backup database schema initialized");

            // Initial data sync could be added here if needed
            // await PerformInitialDataSyncAsync();

            _logger.LogInformation("[BACKUP INIT] Backup database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BACKUP INIT] Error initializing backup database");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[BACKUP INIT] Backup database service stopping");

        try
        {
            if (_backupDb is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BACKUP INIT] Error disposing backup database service");
        }
    }

    private async Task PerformInitialDataSyncAsync()
    {
        // This method could be used to perform an initial data sync
        // from primary to backup database if needed
        _logger.LogInformation("[BACKUP INIT] Initial data sync not implemented - backup will sync via CDC");
    }
}