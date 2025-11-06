using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extension
{
    public static class ManageDbEndpointsExtensions
    {
        public static void MapManageDbEndpointsAsync(this WebApplication app)
        {
            var adminApi = app.MapGroup("/admin")
            .RequireAuthorization()
            .WithTags(["管理"]);

            adminApi.MapGet("/database/exists/{name}", async (string name, DatabaseManagerService dbManager) => {
                var exists = await dbManager.DatabaseExistsAsync(name);
                return Results.Ok(new { database = name, exists = exists });
            });

            adminApi.MapPost("/database/drop/{name}", async (string name, DatabaseManagerService dbManager) =>
            {
                var success = await dbManager.DropDatabaseAsync(name);
                return success
                    ? Results.Ok(new { message = $"数据库 {name} 已删除" })
                    : Results.Problem("删除数据库失败");
            });

            adminApi.MapPost("/database/create/{name}", async (string name, DatabaseManagerService dbManager) =>
            {
                var success = await dbManager.CreateDatabaseAsync(name);
                return success
                    ? Results.Ok(new { message = $"数据库 {name} 已创建" })
                    : Results.Problem("创建数据库失败");
            });

            adminApi.MapPost("/database/reset/{name}", async (string name, DatabaseManagerService dbManager) =>
            {
                // 先删除
                await dbManager.DropDatabaseAsync(name);
                // 再创建
                var success = await dbManager.CreateDatabaseAsync(name);
                return success
                    ? Results.Ok(new { message = $"数据库 {name} 已重置" })
                    : Results.Problem("重置数据库失败");
            });
        }
    }
}
