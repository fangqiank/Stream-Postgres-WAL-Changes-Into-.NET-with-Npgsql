using Npgsql;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    public class ReplicationSlotManagerService(
        IConfiguration configuration,
        ILogger<ReplicationSlotManagerService> logger
    )
    {
        public async Task<bool> ResetReplicationSlotAsync(string slotName = "wal_sync_slot")
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if slot exists
                await using var checkCmd = new NpgsqlCommand(
                    "SELECT slot_name, active, restart_lsn FROM pg_replication_slots WHERE slot_name = $1",
                    connection);
                checkCmd.Parameters.AddWithValue(slotName);

                var slotInfo = await checkCmd.ExecuteScalarAsync();
                if (slotInfo == null)
                {
                    logger.LogInformation("Replication slot {SlotName} does not exist, creating it", slotName);
                    await CreateReplicationSlotAsync(connection, slotName);
                    return true;
                }

                // Get slot details
                await using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var isActive = reader.GetBoolean(1);
                    var restartLsn = reader.IsDBNull(2) ? "N/A" : reader.GetString(2);

                    if (isActive)
                    {
                        logger.LogWarning("Replication slot {SlotName} is active and cannot be reset", slotName);
                        return false;
                    }

                    // Drop and recreate the slot
                    await using var dropCmd = new NpgsqlCommand(
                        $"SELECT pg_drop_replication_slot('{slotName}')", connection);
                    await dropCmd.ExecuteNonQueryAsync();

                    logger.LogInformation("Dropped replication slot {SlotName}", slotName);

                    await CreateReplicationSlotAsync(connection, slotName);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reset replication slot {SlotName}", slotName);
                return false;
            }
        }

        private async Task CreateReplicationSlotAsync(NpgsqlConnection connection, string slotName)
        {
            await using var createCmd = new NpgsqlCommand(
                $"SELECT pg_create_logical_replication_slot('{slotName}', 'test_decoding')", connection);
            var newLsn = await createCmd.ExecuteScalarAsync();

            logger.LogInformation("Created replication slot {SlotName} with LSN {LSN}", slotName, newLsn?.ToString() ?? "N/A");
        }

        public async Task<bool> CreatePublicationIfNotExistsAsync(string publicationName = "cdc_publication")
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if publication exists
                await using var checkCmd = new NpgsqlCommand(
                    "SELECT pubname FROM pg_publication WHERE pubname = $1",
                    connection);
                checkCmd.Parameters.AddWithValue(publicationName);

                var existingPub = await checkCmd.ExecuteScalarAsync();
                if (existingPub == null)
                {
                    // Create publication
                    await using var createCmd = new NpgsqlCommand(
                        "CREATE PUBLICATION cdc_publication FOR TABLE \"Orders\", \"OutboxEvents\"",
                        connection);
                    await createCmd.ExecuteNonQueryAsync();

                    logger.LogInformation("Created CDC publication {PublicationName}", publicationName);
                }
                else
                {
                    logger.LogInformation("CDC publication {PublicationName} already exists", publicationName);
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create CDC publication {PublicationName}", publicationName);
                return false;
            }
        }

        public async Task<bool> ForceCleanupReplicationSlotAsync(string slotName = "wal_sync_slot")
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                // Terminate any processes using the replication slot
                await using var terminateCmd = new NpgsqlCommand(
                    @"SELECT pg_terminate_backend(pid)
                      FROM pg_replication_slots r
                      JOIN pg_stat_activity a ON r.active_pid = a.pid
                      WHERE r.slot_name = $1 AND r.active = true",
                    connection);
                terminateCmd.Parameters.AddWithValue(slotName);

                var terminatedProcesses = await terminateCmd.ExecuteNonQueryAsync();
                if (terminatedProcesses > 0)
                {
                    logger.LogInformation("Terminated {Count} processes using replication slot {SlotName}", terminatedProcesses, slotName);
                    await Task.Delay(2000); // Wait for processes to fully terminate
                }

                // Now try to drop the slot
                await using var dropCmd = new NpgsqlCommand(
                    $"SELECT pg_drop_replication_slot('{slotName}')", connection);
                await dropCmd.ExecuteNonQueryAsync();

                logger.LogInformation("Force dropped replication slot {SlotName}", slotName);

                // Recreate the slot
                await CreateReplicationSlotAsync(connection, slotName);

                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to force cleanup replication slot {SlotName}", slotName);
                return false;
            }
        }

        public async Task<Dictionary<string, object>> GetReplicationSlotStatusAsync(string slotName = "wal_sync_slot")
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var result = new Dictionary<string, object>();

            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                await using var cmd = new NpgsqlCommand(
                    @"SELECT
                        slot_name,
                        plugin,
                        slot_type,
                        database,
                        active,
                        active_pid,
                        restart_lsn,
                        confirmed_flush_lsn,
                        wal_status
                      FROM pg_replication_slots
                      WHERE slot_name = $1",
                    connection);
                cmd.Parameters.AddWithValue(slotName);

                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    result["SlotName"] = reader.GetString(0);
                    result["Plugin"] = reader.GetString(1);
                    result["SlotType"] = reader.GetString(2);
                    result["Database"] = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3);
                    result["IsActive"] = reader.GetBoolean(4);
                    #pragma warning disable CS8601
                    result["ActivePid"] = reader.IsDBNull(5) ? null : (object)reader.GetInt32(5);
#pragma warning restore CS8601
                    result["RestartLsn"] = reader.IsDBNull(6) ? string.Empty : reader.GetValue(6)?.ToString() ?? string.Empty;
                    result["ConfirmedFlushLsn"] = reader.IsDBNull(7) ? string.Empty : reader.GetValue(7)?.ToString() ?? string.Empty;
                    result["WalStatus"] = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                    result["Timestamp"] = DateTime.UtcNow;
                }
                else
                {
                    result["Exists"] = false;
                    result["Message"] = $"Replication slot {slotName} not found";
                    result["Timestamp"] = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                result["Error"] = ex.Message;
                result["Timestamp"] = DateTime.UtcNow;
            }

            return result;
        }
    }
}