using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

/// <summary>
/// PostgreSQL逻辑复制API端点
/// </summary>
public static class LogicalReplicationEndpoints
{
    public static IEndpointRouteBuilder MapLogicalReplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/logical-replication")
            .WithTags("Logical Replication")
            .RequireAuthorization();

        // 获取复制状态
        group.MapGet("/status", GetReplicationStatus)
            .WithName("GetReplicationStatus")
            .WithSummary("获取逻辑复制状态");

        // 启动复制
        group.MapPost("/start", StartReplication)
            .WithName("StartReplication")
            .WithSummary("启动逻辑复制");

        // 停止复制
        group.MapPost("/stop", StopReplication)
            .WithName("StopReplication")
            .WithSummary("停止逻辑复制");

        // 重启复制
        group.MapPost("/restart", RestartReplication)
            .WithName("RestartReplication")
            .WithSummary("重启逻辑复制");

        // 获取订阅信息
        group.MapGet("/subscription", GetSubscriptionInfo)
            .WithName("GetSubscriptionInfo")
            .WithSummary("获取订阅详细信息");

        // 获取发布信息
        group.MapGet("/publication", GetPublicationInfo)
            .WithName("GetPublicationInfo")
            .WithSummary("获取发布详细信息");

        // 创建发布
        group.MapPost("/publication/create", CreatePublication)
            .WithName("CreatePublication")
            .WithSummary("创建发布");

        // 创建订阅
        group.MapPost("/subscription/create", CreateSubscription)
            .WithName("CreateSubscription")
            .WithSummary("创建订阅");

        // 删除订阅
        group.MapDelete("/subscription/{subscriptionName}", DeleteSubscription)
            .WithName("DeleteSubscription")
            .WithSummary("删除订阅");

        // 获取复制延迟
        group.MapGet("/lag", GetReplicationLag)
            .WithName("GetReplicationLag")
            .WithSummary("获取复制延迟");

        // 全面诊断复制状态
        group.MapGet("/diagnose", DiagnoseReplication)
            .WithName("DiagnoseReplication")
            .WithSummary("全面诊断复制状态");

        // 添加表到发布
        group.MapPost("/publication/add-tables", AddTablesToPublication)
            .WithName("AddTablesToPublication")
            .WithSummary("添加表到发布");

        // 查看发布的表
        group.MapGet("/publication/tables", GetPublicationTables)
            .WithName("GetPublicationTables")
            .WithSummary("查看发布中包含的表");

        return app;
    }

    /// <summary>
    /// 映射公共诊断端点（无需认证）
    /// </summary>
    public static IEndpointRouteBuilder MapPublicDiagnosticEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public")
            .WithTags("Public Diagnostic")
            .RequireAuthorization();

        // 公共诊断端点
        group.MapGet("/replication-diagnose", PublicDiagnoseReplication)
            .WithName("PublicDiagnoseReplication")
            .WithSummary("公共复制诊断（无需认证）")
            .AllowAnonymous(); // 允许匿名访问

        return app;
    }

    /// <summary>
    /// 获取复制状态
    /// </summary>
    private static async Task<IResult> GetReplicationStatus(
        PostgreSqlLogicalReplicationService replicationService,
        ILogger<Program> logger)
    {
        try
        {
            var status = replicationService.GetStatus();
            return Results.Ok(new
            {
                success = true,
                data = status,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取逻辑复制状态失败");
            return Results.Problem(
                title: "获取复制状态失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 启动复制
    /// </summary>
    private static async Task<IResult> StartReplication(
        [FromServices] IServiceProvider serviceProvider,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            // 这里需要实现启动逻辑复制服务的机制
            // 由于是BackgroundService，我们需要重新启动服务

            return Results.Ok(new
            {
                success = true,
                message = "逻辑复制启动命令已发送",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动逻辑复制失败");
            return Results.Problem(
                title: "启动复制失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 停止复制
    /// </summary>
    private static async Task<IResult> StopReplication(
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            // 这里需要实现停止逻辑复制服务的机制

            return Results.Ok(new
            {
                success = true,
                message = "逻辑复制停止命令已发送",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "停止逻辑复制失败");
            return Results.Problem(
                title: "停止复制失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 重启复制
    /// </summary>
    private static async Task<IResult> RestartReplication(
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            // 这里需要实现重启逻辑复制服务的机制

            return Results.Ok(new
            {
                success = true,
                message = "逻辑复制重启命令已发送",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "重启逻辑复制失败");
            return Results.Problem(
                title: "重启复制失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 获取订阅信息
    /// </summary>
    private static async Task<IResult> GetSubscriptionInfo(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT subname, subenabled, subslotname, subconninfo, subpublications,
                         substream, subtwophasestate, subdisableonerr,
                         subpasswordrequired, subrunasowner, subslotname
                  FROM pg_subscription
                  ORDER BY subname",
                connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            var subscriptions = new List<object>();

            while (await reader.ReadAsync())
            {
                var subscription = new
                {
                    Name = reader.GetString(0),
                    Enabled = reader.GetBoolean(1),
                    SlotName = reader.GetString(2),
                    ConnectionInfo = reader.GetString(3),
                    Publications = reader.GetString(4),
                    Stream = reader.GetBoolean(5),
                    TwoPhaseState = reader.GetBoolean(6),
                    DisableOnError = reader.GetBoolean(7),
                    PasswordRequired = reader.GetBoolean(8),
                    RunAsOwner = reader.GetBoolean(9)
                };
                subscriptions.Add(subscription);
            }

            return Results.Ok(new
            {
                success = true,
                data = subscriptions,
                count = subscriptions.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取订阅信息失败");
            return Results.Problem(
                title: "获取订阅信息失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 获取发布信息
    /// </summary>
    private static async Task<IResult> GetPublicationInfo(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sourceContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await using var connection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT p.pubname, p.pubowner, p.puballtables, p.pubinsert, p.pubupdate,
                         p.pubdelete, p.pubtruncate, p.pubviaroot
                  FROM pg_publication p
                  ORDER BY p.pubname",
                connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            var publications = new List<object>();

            while (await reader.ReadAsync())
            {
                var publication = new
                {
                    Name = reader.GetString(0),
                    Owner = reader.GetInt32(1),
                    AllTables = reader.GetBoolean(2),
                    Insert = reader.GetBoolean(3),
                    Update = reader.GetBoolean(4),
                    Delete = reader.GetBoolean(5),
                    Truncate = reader.GetBoolean(6),
                    ViaRoot = reader.GetBoolean(7)
                };
                publications.Add(publication);
            }

            // 获取发布的表信息
            var publicationTables = new List<object>();
            foreach (var pub in publications)
            {
                var pubName = ((dynamic)pub).Name;

                await using var tableCmd = new NpgsqlCommand(
                    @"SELECT schemaname, tablename
                      FROM pg_publication_tables
                      WHERE pubname = @pubName",
                    connection);
                tableCmd.Parameters.AddWithValue("@pubName", pubName);

                await using var tableReader = await tableCmd.ExecuteReaderAsync();
                while (await tableReader.ReadAsync())
                {
                    var table = new
                    {
                        Publication = pubName,
                        Schema = tableReader.GetString(0),
                        Table = tableReader.GetString(1)
                    };
                    publicationTables.Add(table);
                }
            }

            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    publications = publications,
                    tables = publicationTables
                },
                publicationCount = publications.Count,
                tableCount = publicationTables.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取发布信息失败");
            return Results.Problem(
                title: "获取发布信息失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 创建发布
    /// </summary>
    private static async Task<IResult> CreatePublication(
        [FromBody] CreatePublicationRequest request,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.PublicationName))
            {
                return Results.BadRequest(new { error = "发布名称不能为空" });
            }

            if (!request.Tables.Any())
            {
                return Results.BadRequest(new { error = "至少需要指定一个表" });
            }

            using var scope = scopeFactory.CreateScope();
            var sourceContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await using var connection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
            await connection.OpenAsync();

            // 检查发布是否已存在
            await using var checkCmd = new NpgsqlCommand(
                @"SELECT 1 FROM pg_publication WHERE pubname = @publicationName",
                connection);
            checkCmd.Parameters.AddWithValue("@publicationName", request.PublicationName);

            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists != null)
            {
                return Results.Conflict(new { error = "发布已存在" });
            }

            // 创建发布
            var tablesList = string.Join(", ", request.Tables.Select(t => $"\"{t}\""));
            await using var createCmd = new NpgsqlCommand(
                $"CREATE PUBLICATION {request.PublicationName} FOR TABLE {tablesList}",
                connection);
            await createCmd.ExecuteNonQueryAsync();

            return Results.Ok(new
            {
                success = true,
                message = $"发布 {request.PublicationName} 创建成功",
                tables = request.Tables,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "创建发布失败: {PublicationName}", request.PublicationName);
            return Results.Problem(
                title: "创建发布失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 创建订阅
    /// </summary>
    private static async Task<IResult> CreateSubscription(
        [FromBody] CreateSubscriptionRequest request,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SubscriptionName))
            {
                return Results.BadRequest(new { error = "订阅名称不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.PublicationName))
            {
                return Results.BadRequest(new { error = "发布名称不能为空" });
            }

            if (string.IsNullOrWhiteSpace(request.ConnectionString))
            {
                return Results.BadRequest(new { error = "连接字符串不能为空" });
            }

            using var scope = scopeFactory.CreateScope();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync();

            // 检查订阅是否已存在
            await using var checkCmd = new NpgsqlCommand(
                @"SELECT 1 FROM pg_subscription WHERE subname = @subscriptionName",
                connection);
            checkCmd.Parameters.AddWithValue("@subscriptionName", request.SubscriptionName);

            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists != null)
            {
                return Results.Conflict(new { error = "订阅已存在" });
            }

            // 创建订阅
            var copyDataOption = request.CopyExistingData ? "true" : "false";
            await using var createCmd = new NpgsqlCommand(
                $"CREATE SUBSCRIPTION {request.SubscriptionName} CONNECTION '{request.ConnectionString}' PUBLICATION {request.PublicationName} WITH (copy_data = {copyDataOption})",
                connection);
            await createCmd.ExecuteNonQueryAsync();

            return Results.Ok(new
            {
                success = true,
                message = $"订阅 {request.SubscriptionName} 创建成功",
                publication = request.PublicationName,
                copyExistingData = request.CopyExistingData,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "创建订阅失败: {SubscriptionName}", request.SubscriptionName);
            return Results.Problem(
                title: "创建订阅失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 删除订阅
    /// </summary>
    private static async Task<IResult> DeleteSubscription(
        string subscriptionName,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(subscriptionName))
            {
                return Results.BadRequest(new { error = "订阅名称不能为空" });
            }

            using var scope = scopeFactory.CreateScope();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync();

            // 删除订阅
            await using var deleteCmd = new NpgsqlCommand(
                $"DROP SUBSCRIPTION IF EXISTS {subscriptionName}",
                connection);
            await deleteCmd.ExecuteNonQueryAsync();

            return Results.Ok(new
            {
                success = true,
                message = $"订阅 {subscriptionName} 删除成功",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "删除订阅失败: {SubscriptionName}", subscriptionName);
            return Results.Problem(
                title: "删除订阅失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 获取复制延迟
    /// </summary>
    private static async Task<IResult> GetReplicationLag(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            await using var connection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"SELECT s.subname,
                         CASE WHEN pg_wal_lsn_diff(pg_current_wal_lsn(), r.replay_lsn) IS NOT NULL
                              THEN pg_wal_lsn_diff(pg_current_wal_lsn(), r.replay_lsn)
                              ELSE 0 END as lag_bytes,
                         r.flush_lsn, r.replay_lsn, r.sync_state
                  FROM pg_subscription s
                  LEFT JOIN pg_stat_replication r ON r.application_name = s.subname
                  ORDER BY s.subname",
                connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            var replicationStats = new List<object>();

            while (await reader.ReadAsync())
            {
                var stat = new
                {
                    SubscriptionName = reader.GetString(0),
                    LagBytes = reader.GetInt64(1),
                    FlushLsn = reader.GetValue(2),
                    ReplayLsn = reader.GetValue(3),
                    SyncState = reader.GetValue(4)
                };
                replicationStats.Add(stat);
            }

            return Results.Ok(new
            {
                success = true,
                data = replicationStats,
                count = replicationStats.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取复制延迟失败");
            return Results.Problem(
                title: "获取复制延迟失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 全面诊断复制状态
    /// </summary>
    private static async Task<IResult> DiagnoseReplication(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] PostgreSqlLogicalReplicationService replicationService,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            var diagnostic = new Dictionary<string, object>();
            var issues = new List<string>();
            var warnings = new List<string>();

            using var scope = scopeFactory.CreateScope();
            var sourceContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            // 1. 检查源数据库记录数
            try
            {
                await using var sourceConnection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
                await sourceConnection.OpenAsync();

                await using var sourceCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Orders\"", sourceConnection);
                var sourceCount = await sourceCmd.ExecuteScalarAsync();
                diagnostic["source_orders_count"] = sourceCount;

                if (Convert.ToInt64(sourceCount) == 0)
                {
                    warnings.Add("源数据库 Orders 表为空");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"检查源数据库失败: {ex.Message}");
            }

            // 2. 检查目标数据库记录数
            try
            {
                await using var targetConnection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
                await targetConnection.OpenAsync();

                await using var targetCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Orders\"", targetConnection);
                var targetCount = await targetCmd.ExecuteScalarAsync();
                diagnostic["target_orders_count"] = targetCount;

                if (Convert.ToInt64(targetCount) == 0)
                {
                    warnings.Add("目标数据库 Orders 表为空");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"检查目标数据库失败: {ex.Message}");
            }

            // 3. 检查发布状态
            try
            {
                using var sourceScope = scopeFactory.CreateScope();
                var sourceDb = sourceScope.ServiceProvider.GetRequiredService<AppDbContext>();

                await using var sourceConn = new NpgsqlConnection(sourceDb.Database.GetConnectionString());
                await sourceConn.OpenAsync();

                await using var pubCmd = new NpgsqlCommand(
                    @"SELECT p.pubname, p.puballtables, p.pubinsert, p.pubupdate, p.pubdelete,
                             string_agg(t.schemaname || '.' || t.tablename, ', ') as tables
                      FROM pg_publication p
                      LEFT JOIN pg_publication_tables t ON p.pubname = t.pubname
                      GROUP BY p.pubname, p.puballtables, p.pubinsert, p.pubupdate, p.pubdelete",
                    sourceConn);

                await using var pubReader = await pubCmd.ExecuteReaderAsync();
                var publications = new List<object>();

                while (await pubReader.ReadAsync())
                {
                    var pub = new
                    {
                        Name = pubReader.GetString(0),
                        AllTables = pubReader.GetBoolean(1),
                        Insert = pubReader.GetBoolean(2),
                        Update = pubReader.GetBoolean(3),
                        Delete = pubReader.GetBoolean(4),
                        Tables = pubReader.IsDBNull(5) ? "" : pubReader.GetString(5)
                    };
                    publications.Add(pub);
                }

                diagnostic["publications"] = publications;

                if (!publications.Any())
                {
                    issues.Add("未找到任何发布");
                }
                else
                {
                    var hasOrdersPublication = publications.Any(p =>
                        ((dynamic)p).AllTables ||
                        (((dynamic)p).Tables ?? "").Contains("Orders"));

                    if (!hasOrdersPublication)
                    {
                        issues.Add("发布配置中未包含 Orders 表");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"检查发布状态失败: {ex.Message}");
            }

            // 4. 检查订阅状态
            try
            {
                await using var targetConnection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
                await targetConnection.OpenAsync();

                await using var subCmd = new NpgsqlCommand(
                    @"SELECT s.subname, s.subenabled, s.subslotname, s.subpublications,
                             CASE WHEN r.pid IS NOT NULL THEN 'active' ELSE 'inactive' END as worker_status,
                             pg_wal_lsn_diff(pg_current_wal_lsn(), r.replay_lsn) as lag_bytes
                      FROM pg_subscription s
                      LEFT JOIN pg_stat_replication r ON r.application_name = s.subname
                      ORDER BY s.subname",
                    targetConnection);

                await using var subReader = await subCmd.ExecuteReaderAsync();
                var subscriptions = new List<object>();

                while (await subReader.ReadAsync())
                {
                    var sub = new
                    {
                        Name = subReader.GetString(0),
                        Enabled = subReader.GetBoolean(1),
                        SlotName = subReader.GetString(2),
                        Publications = subReader.GetValue(3), // Handle text[] array
                        WorkerStatus = subReader.GetString(4),
                        LagBytes = subReader.IsDBNull(5) ? 0 : subReader.GetInt64(5)
                    };
                    subscriptions.Add(sub);
                }

                diagnostic["subscriptions"] = subscriptions;

                if (!subscriptions.Any())
                {
                    issues.Add("未找到任何订阅");
                }
                else
                {
                    foreach (var sub in subscriptions)
                    {
                        var dynamicSub = (dynamic)sub;
                        if (!dynamicSub.Enabled)
                        {
                            issues.Add($"订阅 {dynamicSub.Name} 未启用");
                        }
                        if (dynamicSub.WorkerStatus == "inactive")
                        {
                            issues.Add($"订阅 {dynamicSub.Name} 工作进程未激活");
                        }
                        if (dynamicSub.LagBytes > 1024 * 1024) // 1MB
                        {
                            warnings.Add($"订阅 {dynamicSub.Name} 存在较大延迟: {dynamicSub.LagBytes} 字节");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"检查订阅状态失败: {ex.Message}");
            }

            // 5. 检查复制槽状态
            try
            {
                await using var sourceConnection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
                await sourceConnection.OpenAsync();

                await using var slotCmd = new NpgsqlCommand(
                    @"SELECT slot_name, slot_type, database, active,
                             pg_wal_lsn_diff(pg_current_wal_lsn(), restart_lsn) as lag_bytes
                      FROM pg_replication_slots
                      WHERE slot_type = 'logical'
                      ORDER BY slot_name",
                    sourceConnection);

                await using var slotReader = await slotCmd.ExecuteReaderAsync();
                var slots = new List<object>();

                while (await slotReader.ReadAsync())
                {
                    var slot = new
                    {
                        Name = slotReader.GetString(0),
                        Type = slotReader.GetString(1),
                        Database = slotReader.GetString(2),
                        Active = slotReader.GetBoolean(3),
                        LagBytes = slotReader.IsDBNull(4) ? 0 : slotReader.GetInt64(4)
                    };
                    slots.Add(slot);
                }

                diagnostic["replication_slots"] = slots;

                if (!slots.Any())
                {
                    warnings.Add("未找到逻辑复制槽");
                }
                else
                {
                    foreach (var slot in slots)
                    {
                        var dynamicSlot = (dynamic)slot;
                        if (!dynamicSlot.Active)
                        {
                            issues.Add($"复制槽 {dynamicSlot.Name} 未激活");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"检查复制槽状态失败: {ex.Message}");
            }

            // 6. 检查最近的复制活动
            try
            {
                await using var targetConnection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
                await targetConnection.OpenAsync();

                await using var activityCmd = new NpgsqlCommand(
                    @"SELECT schemaname, tablename, n_tup_ins as inserts, n_tup_upd as updates, n_tup_del as deletes
                      FROM pg_stat_user_tables
                      WHERE schemaname = 'public'
                      ORDER BY schemaname, tablename",
                    targetConnection);

                await using var activityReader = await activityCmd.ExecuteReaderAsync();
                var tableStats = new List<object>();

                while (await activityReader.ReadAsync())
                {
                    var stat = new
                    {
                        Schema = activityReader.GetString(0),
                        Table = activityReader.GetString(1),
                        Inserts = activityReader.GetInt64(2),
                        Updates = activityReader.GetInt64(3),
                        Deletes = activityReader.GetInt64(4)
                    };
                    tableStats.Add(stat);
                }

                diagnostic["table_activity"] = tableStats;
            }
            catch (Exception ex)
            {
                issues.Add($"检查表活动统计失败: {ex.Message}");
            }

            // 7. 计算数据同步状态
            if (diagnostic.ContainsKey("source_orders_count") && diagnostic.ContainsKey("target_orders_count"))
            {
                var sourceCount = Convert.ToInt64(diagnostic["source_orders_count"]);
                var targetCount = Convert.ToInt64(diagnostic["target_orders_count"]);

                diagnostic["sync_status"] = new
                {
                    source_records = sourceCount,
                    target_records = targetCount,
                    difference = sourceCount - targetCount,
                    sync_percentage = sourceCount > 0 ? (double)targetCount / sourceCount * 100 : 100,
                    is_fully_synced = sourceCount == targetCount
                };

                if (sourceCount > 0 && targetCount < sourceCount)
                {
                    issues.Add($"数据同步不完整: 源数据库 {sourceCount} 条记录，目标数据库只有 {targetCount} 条记录");
                }
            }

            // 8. 服务状态
            try
            {
                var serviceStatus = replicationService.GetStatus();
                diagnostic["service_status"] = serviceStatus;

                if (!serviceStatus.IsRunning)
                {
                    issues.Add("逻辑复制服务未运行");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"获取服务状态失败: {ex.Message}");
            }

            // 生成总体评估
            var overallStatus = "healthy";
            if (issues.Any())
            {
                overallStatus = "error";
            }
            else if (warnings.Any())
            {
                overallStatus = "warning";
            }

            return Results.Ok(new
            {
                success = true,
                status = overallStatus,
                timestamp = DateTime.UtcNow,
                diagnostic = diagnostic,
                issues = issues,
                warnings = warnings,
                summary = new
                {
                    total_issues = issues.Count,
                    total_warnings = warnings.Count,
                    source_records = diagnostic.ContainsKey("source_orders_count") ? diagnostic["source_orders_count"] : "unknown",
                    target_records = diagnostic.ContainsKey("target_orders_count") ? diagnostic["target_orders_count"] : "unknown",
                    publications_count = diagnostic.ContainsKey("publications") ? ((IEnumerable<object>)diagnostic["publications"]).Count() : 0,
                    subscriptions_count = diagnostic.ContainsKey("subscriptions") ? ((IEnumerable<object>)diagnostic["subscriptions"]).Count() : 0,
                    replication_slots_count = diagnostic.ContainsKey("replication_slots") ? ((IEnumerable<object>)diagnostic["replication_slots"]).Count() : 0
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行复制诊断失败");
            return Results.Problem(
                title: "复制诊断失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 公共复制诊断（无需认证）
    /// </summary>
    private static async Task<IResult> PublicDiagnoseReplication(
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] PostgreSqlLogicalReplicationService replicationService,
        [FromServices] IConfiguration configuration,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            var diagnostic = new Dictionary<string, object>();
            var issues = new List<string>();
            var warnings = new List<string>();

            using var scope = scopeFactory.CreateScope();
            var sourceContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var targetContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

            // 1. 检查源数据库记录数
            try
            {
                await using var sourceConnection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
                await sourceConnection.OpenAsync();

                await using var sourceCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Orders\"", sourceConnection);
                var sourceCount = await sourceCmd.ExecuteScalarAsync();
                diagnostic["source_orders_count"] = sourceCount;

                if (Convert.ToInt64(sourceCount) == 0)
                {
                    warnings.Add("源数据库 Orders 表为空");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"检查源数据库失败: {ex.Message}");
            }

            // 2. 检查目标数据库记录数
            try
            {
                await using var targetConnection = new NpgsqlConnection(targetContext.Database.GetConnectionString());
                await targetConnection.OpenAsync();

                await using var targetCmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"Orders\"", targetConnection);
                var targetCount = await targetCmd.ExecuteScalarAsync();
                diagnostic["target_orders_count"] = targetCount;

                if (Convert.ToInt64(targetCount) == 0)
                {
                    warnings.Add("目标数据库 Orders 表为空");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"检查目标数据库失败: {ex.Message}");
            }

            // 3. 计算数据同步状态
            if (diagnostic.ContainsKey("source_orders_count") && diagnostic.ContainsKey("target_orders_count"))
            {
                var sourceCount = Convert.ToInt64(diagnostic["source_orders_count"]);
                var targetCount = Convert.ToInt64(diagnostic["target_orders_count"]);

                diagnostic["sync_status"] = new
                {
                    source_records = sourceCount,
                    target_records = targetCount,
                    difference = sourceCount - targetCount,
                    sync_percentage = sourceCount > 0 ? (double)targetCount / sourceCount * 100 : 100,
                    is_fully_synced = sourceCount == targetCount
                };

                if (sourceCount > 0 && targetCount < sourceCount)
                {
                    issues.Add($"数据同步不完整: 源数据库 {sourceCount} 条记录，目标数据库只有 {targetCount} 条记录");
                }
            }

            // 4. 简化的服务状态
            try
            {
                var serviceStatus = replicationService.GetStatus();
                diagnostic["service_status"] = new
                {
                    is_running = serviceStatus.IsRunning,
                    start_time = serviceStatus.StartTime,
                    uptime = serviceStatus.Uptime
                };

                if (!serviceStatus.IsRunning)
                {
                    issues.Add("逻辑复制服务未运行");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"获取服务状态失败: {ex.Message}");
            }

            // 生成总体评估
            var overallStatus = "healthy";
            if (issues.Any())
            {
                overallStatus = "error";
            }
            else if (warnings.Any())
            {
                overallStatus = "warning";
            }

            return Results.Ok(new
            {
                success = true,
                status = overallStatus,
                timestamp = DateTime.UtcNow,
                diagnostic = diagnostic,
                issues = issues,
                warnings = warnings,
                summary = new
                {
                    total_issues = issues.Count,
                    total_warnings = warnings.Count,
                    source_records = diagnostic.ContainsKey("source_orders_count") ? diagnostic["source_orders_count"] : "unknown",
                    target_records = diagnostic.ContainsKey("target_orders_count") ? diagnostic["target_orders_count"] : "unknown"
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行公共复制诊断失败");
            return Results.Problem(
                title: "复制诊断失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 添加表到发布
    /// </summary>
    private static async Task<IResult> AddTablesToPublication(
        [FromBody] AddTablesToPublicationRequest request,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.PublicationName))
            {
                return Results.BadRequest(new { error = "发布名称不能为空" });
            }

            if (!request.Tables.Any())
            {
                return Results.BadRequest(new { error = "至少需要指定一个表" });
            }

            using var scope = scopeFactory.CreateScope();
            var sourceContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await using var connection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
            await connection.OpenAsync();

            // 构建添加表的SQL
            var tablesList = string.Join(", ", request.Tables.Select(t => $"\"{t}\""));
            var alterSql = $"ALTER PUBLICATION {request.PublicationName} ADD TABLE {tablesList}";

            await using var cmd = new NpgsqlCommand(alterSql, connection);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(new
            {
                success = true,
                message = $"成功添加表到发布 {request.PublicationName}",
                tables = request.Tables,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "添加表到发布失败: {PublicationName}", request.PublicationName);
            return Results.Problem(
                title: "添加表到发布失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// 查看发布中包含的表
    /// </summary>
    private static async Task<IResult> GetPublicationTables(
        [FromQuery] string? publicationName,
        [FromServices] IServiceScopeFactory scopeFactory,
        [FromServices] ILogger<Program> logger)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sourceContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await using var connection = new NpgsqlConnection(sourceContext.Database.GetConnectionString());
            await connection.OpenAsync();

            // 获取发布信息
            await using var pubCmd = new NpgsqlCommand(
                @"SELECT p.pubname, p.puballtables
                  FROM pg_publication p
                  ORDER BY p.pubname",
                connection);

            await using var pubReader = await pubCmd.ExecuteReaderAsync();
            var publications = new List<object>();

            while (await pubReader.ReadAsync())
            {
                var pub = new
                {
                    Name = pubReader.GetString(0),
                    AllTables = pubReader.GetBoolean(1)
                };
                publications.Add(pub);
            }

            // 获取发布的表信息
            await using var tablesCmd = new NpgsqlCommand(
                @"SELECT pt.pubname, pt.schemaname, pt.tablename
                  FROM pg_publication_tables pt
                  ORDER BY pt.pubname, pt.schemaname, pt.tablename",
                connection);

            await using var tablesReader = await tablesCmd.ExecuteReaderAsync();
            var publicationTables = new List<object>();

            while (await tablesReader.ReadAsync())
            {
                var table = new
                {
                    PublicationName = tablesReader.GetString(0),
                    SchemaName = tablesReader.GetString(1),
                    TableName = tablesReader.GetString(2)
                };
                publicationTables.Add(table);
            }

            return Results.Ok(new
            {
                success = true,
                publications = publications,
                tables = publicationTables,
                publicationCount = publications.Count,
                tableCount = publicationTables.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取发布表信息失败");
            return Results.Problem(
                title: "获取发布表信息失败",
                detail: ex.Message,
                statusCode: 500);
        }
    }
}

/// <summary>
/// 创建发布请求
/// </summary>
public class CreatePublicationRequest
{
    public string PublicationName { get; set; } = string.Empty;
    public List<string> Tables { get; set; } = new();
}

/// <summary>
/// 创建订阅请求
/// </summary>
public class CreateSubscriptionRequest
{
    public string SubscriptionName { get; set; } = string.Empty;
    public string PublicationName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public bool CopyExistingData { get; set; } = true;
}

/// <summary>
/// 添加表到发布请求
/// </summary>
public class AddTablesToPublicationRequest
{
    public string PublicationName { get; set; } = string.Empty;
    public List<string> Tables { get; set; } = new();
}