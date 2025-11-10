using Npgsql;

namespace DebeziumDemoApp.Services;

public interface IBackupPostgresService
{
    Task<int> ExecuteNonQueryAsync(string sql, params NpgsqlParameter[] parameters);
    Task<T?> ExecuteScalarAsync<T>(string sql, params NpgsqlParameter[] parameters);
    Task<T> QuerySingleAsync<T>(string sql, Func<NpgsqlDataReader, T> mapper, params NpgsqlParameter[] parameters);
    Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> mapper, params NpgsqlParameter[] parameters);
    Task InitializeDatabaseAsync();
    Task<bool> TestConnectionAsync();
}