using Microsoft.AspNetCore.Mvc;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

public static class ReplicationEndpoints
{
    public static void MapReplicationEndpoints(this WebApplication app)
    {
        // Replication management endpoints - require authorization
        var api = app.MapGroup("/api/replication").RequireAuthorization();

        // Get replication slot status
        api.MapGet("/slot-status", async (ReplicationSlotManagerService slotManager, string slotName = "order_events_slot") =>
        {
            try
            {
                var status = await slotManager.GetReplicationSlotStatusAsync(slotName);
                return Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get slot status: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("GetReplicationSlotStatus")
        .WithSummary("Get Replication Slot Status")
        .WithDescription("Get detailed status of a replication slot")
        .WithTags("Replication");

        // Reset replication slot
        api.MapPost("/reset-slot", async (ReplicationSlotManagerService slotManager, string slotName = "order_events_slot") =>
        {
            var result = new Dictionary<string, object>();

            try
            {
                var success = await slotManager.ResetReplicationSlotAsync(slotName);

                result["Success"] = success;
                result["SlotName"] = slotName;
                result["Message"] = success ? $"Replication slot {slotName} reset successfully" : $"Failed to reset replication slot {slotName}";
                result["Timestamp"] = DateTime.UtcNow;

                return success ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                result["Success"] = false;
                result["Error"] = ex.Message;
                result["Message"] = $"Error resetting replication slot: {ex.Message}";
                result["Timestamp"] = DateTime.UtcNow;

                return Results.Problem($"Reset failed: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("ResetReplicationSlot")
        .WithSummary("Reset Replication Slot")
        .WithDescription("Reset an inactive logical replication slot")
        .WithTags("Replication");

        // Force cleanup replication slot
        api.MapPost("/force-cleanup", async (ReplicationSlotManagerService slotManager, string slotName = "order_events_slot") =>
        {
            var result = new Dictionary<string, object>();

            try
            {
                var success = await slotManager.ForceCleanupReplicationSlotAsync(slotName);

                result["Success"] = success;
                result["SlotName"] = slotName;
                result["Message"] = success ? $"Replication slot {slotName} force cleaned up successfully" : $"Failed to force cleanup replication slot {slotName}";
                result["Timestamp"] = DateTime.UtcNow;

                return success ? Results.Ok(result) : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                result["Success"] = false;
                result["Error"] = ex.Message;
                result["Message"] = $"Error force cleaning up replication slot: {ex.Message}";
                result["Timestamp"] = DateTime.UtcNow;

                return Results.Problem($"Force cleanup failed: {ex.Message}", statusCode: 500);
            }
        })
        .WithName("ForceCleanupReplicationSlot")
        .WithSummary("Force Cleanup Replication Slot")
        .WithDescription("Force cleanup a replication slot by terminating active connections")
        .WithTags("Replication");

        // Diagnose replication issues
        api.MapGet("/diagnose", async (IReplicationHealthMonitor healthMonitor, Microsoft.Extensions.Configuration.IConfiguration config) =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");
            var diagnostics = new Dictionary<string, object>();

            try
            {
                using var connection = new Npgsql.NpgsqlConnection(connectionString);
                await connection.OpenAsync();

                // Check replication slot status
                try
                {
                    await using var cmd = new Npgsql.NpgsqlCommand(
                        @"SELECT
                            slot_name,
                            plugin,
                            slot_type,
                            database,
                            active,
                            active_pid,
                            restart_lsn,
                            confirmed_flush_lsn,
                            wal_status,
                            safe_wal_size
                            FROM pg_replication_slots
                            ORDER BY slot_name",
                        connection);

                    await using var reader = await cmd.ExecuteReaderAsync();
                    var slots = new List<object>();

                    while (await reader.ReadAsync())
                    {
                        slots.Add(new
                        {
                            SlotName = reader.GetString(0),
                            Plugin = reader.GetString(1),
                            SlotType = reader.GetString(2),
                            Database = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                            IsActive = reader.GetBoolean(4),
                            ActivePid = reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                            RestartLsn = reader.IsDBNull(6) ? null : reader.GetValue(6)?.ToString(),
                            ConfirmedFlushLsn = reader.IsDBNull(7) ? null : reader.GetValue(7)?.ToString(),
                            WalStatus = reader.IsDBNull(8) ? null : reader.GetString(8),
                            SafeWalSize = reader.IsDBNull(9) ? null : (long?)reader.GetInt64(9)
                        });
                    }

                    diagnostics["ReplicationSlots"] = slots;
                }
                catch (Exception slotEx)
                {
                    diagnostics["ReplicationSlots"] = new { Error = slotEx.Message, Details = "Failed to query pg_replication_slots" };
                }

                // Check WAL senders
                try
                {
                    await using var cmd2 = new Npgsql.NpgsqlCommand(
                        @"SELECT
                            pid,
                            state,
                            application_name,
                            backend_start,
                            query
                            FROM pg_stat_activity
                            WHERE backend_type = 'walsender'
                            ORDER BY backend_start",
                        connection);

                    await using var reader2 = await cmd2.ExecuteReaderAsync();
                    var walSenders = new List<object>();

                    while (await reader2.ReadAsync())
                    {
                        walSenders.Add(new
                        {
                            Pid = reader2.GetInt32(0),
                            State = reader2.GetString(1),
                            ApplicationName = reader2.IsDBNull(2) ? null : reader2.GetString(2),
                            BackendStart = reader2.GetDateTime(3),
                            Query = reader2.IsDBNull(4) ? null : reader2.GetString(4)
                        });
                    }

                    diagnostics["WalSenders"] = walSenders;
                }
                catch (Exception walEx)
                {
                    diagnostics["WalSenders"] = new { Error = walEx.Message, Details = "Failed to query pg_stat_activity for WAL senders" };
                }

                // Check PostgreSQL settings
                try
                {
                    await using var cmd3 = new Npgsql.NpgsqlCommand(
                        @"SELECT name, setting, short_desc
                            FROM pg_settings
                            WHERE name IN ('wal_level', 'max_wal_senders', 'max_replication_slots', 'archive_mode')
                            ORDER BY name",
                        connection);

                    await using var reader3 = await cmd3.ExecuteReaderAsync();
                    var settings = new Dictionary<string, object>();

                    while (await reader3.ReadAsync())
                    {
                        var name = reader3.GetString(0);
                        var setting = reader3.GetString(1);
                        var description = reader3.GetString(2);

                        settings[name] = new
                        {
                            Value = setting,
                            Description = description
                        };
                    }

                    diagnostics["PostgreSQLSettings"] = settings;
                }
                catch (Exception settingsEx)
                {
                    diagnostics["PostgreSQLSettings"] = new { Error = settingsEx.Message, Details = "Failed to query pg_settings" };
                }

                // Check publication status
                try
                {
                    await using var cmd4 = new Npgsql.NpgsqlCommand(
                        @"SELECT
                            pubname,
                            pubowner,
                            puballtables,
                            pubinsert,
                            pubupdate,
                            pubdelete,
                            pubtruncate
                            FROM pg_publication
                            ORDER BY pubname",
                        connection);

                    await using var reader4 = await cmd4.ExecuteReaderAsync();
                    var publications = new List<object>();

                    while (await reader4.ReadAsync())
                    {
                        publications.Add(new
                        {
                            Name = reader4.GetString(0),
                            Owner = reader4.GetInt32(1),
                            AllTables = reader4.GetBoolean(2),
                            Insert = reader4.GetBoolean(3),
                            Update = reader4.GetBoolean(4),
                            Delete = reader4.GetBoolean(5),
                            Truncate = reader4.GetBoolean(6)
                        });
                    }

                    diagnostics["Publications"] = publications;
                }
                catch (Exception pubEx)
                {
                    diagnostics["Publications"] = new { Error = pubEx.Message, Details = "Failed to query pg_publication" };
                }

                diagnostics["ConnectionInfo"] = new
                {
                    Host = connection.Host,
                    Database = connection.Database,
                    Username = connection.UserName,
                    State = connection.State.ToString()
                };

                diagnostics["Timestamp"] = DateTime.UtcNow;

                return Results.Ok(diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics["Error"] = ex.Message;
                diagnostics["Timestamp"] = DateTime.UtcNow;
                diagnostics["StackTrace"] = ex.StackTrace;
                return Results.Ok(diagnostics);
            }
        })
        .WithName("ReplicationDiagnosis")
        .WithSummary("Replication Diagnosis")
        .WithDescription("Get detailed PostgreSQL replication diagnostic information")
        .WithTags("Replication");
    }
}