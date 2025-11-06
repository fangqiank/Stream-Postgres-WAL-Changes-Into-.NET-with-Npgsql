using Npgsql;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    public class DatabaseManagerService(
        IConfiguration configuration,
        ILogger<DatabaseManagerService> logger
        )
    {
        public async Task<bool> DropDatabaseAsync(string databaseName)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var baseConnectionString = connectionString.Replace(databaseName, "postgres");

            try
            {
                using var connection = new NpgsqlConnection(baseConnectionString);
                await connection.OpenAsync();

                await TerminateDatabaseConnection(connection, databaseName);

                await using var cmd = new NpgsqlCommand(
                    $"DROP DATABASE IF EXISTS {databaseName}", connection
                    );

                await cmd.ExecuteNonQueryAsync();

                logger.LogInformation("数据库 {DatabaseName} 已删除", databaseName);
                return true;
            }
            catch (Exception ex) 
            {
                logger.LogError(ex, "删除数据库 {DatabaseName} 失败", databaseName);
                return false;
            }
        }

        private async Task TerminateDatabaseConnection(NpgsqlConnection connection, string databaseName)
        {
            try
            {
                await using var cmd = new NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @dbName AND pid <> pg_backend_pid()"
                    , connection);

                cmd.Parameters.AddWithValue("dbName", databaseName);
                await cmd.ExecuteNonQueryAsync();

                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "终止数据库连接时发生错误");
            }
        }

        public async Task<bool> CreateDatabaseAsync(string databaseName)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var baseConnectionString = connectionString!.Replace(databaseName, "postgres");

            try
            {
                using var connection = new NpgsqlConnection(baseConnectionString);
                await connection.OpenAsync();

                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
                await cmd.ExecuteNonQueryAsync();

                logger.LogInformation("创建数据库 {DatabaseName} 失败", databaseName);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "创建数据库 {DatabaseName} 失败", databaseName); 
                return false;
            }
        }

        public async Task<bool> DatabaseExistsAsync(string databaseName)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var baseConnectionString = connectionString!.Replace(databaseName, "postgres");

            try
            {
                using var connection = new NpgsqlConnection(baseConnectionString);
                await connection.OpenAsync();

                await using var cmd = new NpgsqlCommand(
                    "SELECT 1 FROM pg_database WHERE datname = @dbName",
                    connection);
                cmd.Parameters.AddWithValue("dbName", databaseName);

                var result = await cmd.ExecuteNonQueryAsync();
                return result != null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "检查数据库是否存在时失败");
                return false;
            }
        }
    }
}
