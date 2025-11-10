using DebeziumDemoApp.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Diagnostics;

namespace DebeziumDemoApp.Core.DataTargets;

/// <summary>
/// MongoDB数据目标实现，支持向MongoDB集合写入数据
/// </summary>
public class MongoDBDataTarget : IDataTarget
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MongoDBDataTarget> _logger;
    private IMongoClient? _mongoClient;
    private IMongoDatabase? _database;
    private readonly string _connectionString;
    private readonly string _databaseName;
    private bool _isConnected;
    private readonly DataTargetStatistics _statistics = new();

    public string Name { get; }
    public DataTargetType Type => DataTargetType.MongoDB;
    public bool IsConnected => _isConnected && _mongoClient != null && _database != null;
    public HashSet<Type> SupportedTypes { get; } = new();

    public event EventHandler<DataWriteEventArgs>? OnWrite;

    public MongoDBDataTarget(
        string name,
        IConfiguration configuration,
        ILogger<MongoDBDataTarget> logger)
    {
        Name = name;
        _configuration = configuration;
        _logger = logger;

        _connectionString = _configuration[$"DataTargets:{name}:ConnectionString"]
                           ?? "mongodb://localhost:27017";

        _databaseName = _configuration[$"DataTargets:{name}:DatabaseName"]
                       ?? "debezium_sync";

        // 添加支持的基本类型
        SupportedTypes.Add(typeof(object));
        SupportedTypes.Add(typeof(Dictionary<string, object>));
        SupportedTypes.Add(typeof(BsonDocument));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isConnected)
            {
                _logger.LogWarning("[MONGO_TARGET] Already connected to MongoDB target");
                return;
            }

            var clientSettings = MongoClientSettings.FromConnectionString(_connectionString);
            clientSettings.ConnectTimeout = TimeSpan.FromSeconds(30);
            clientSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(30);

            _mongoClient = new MongoClient(clientSettings);
            _database = _mongoClient.GetDatabase(_databaseName);

            // 测试连接
            await _database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: cancellationToken);

            _isConnected = true;
            _logger.LogInformation("[MONGO_TARGET] Connected to MongoDB database: {Database}", _databaseName);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MONGO_TARGET] Failed to connect to MongoDB target");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_isConnected)
            {
                return;
            }

            // MongoDB驱动会自动管理连接池，我们只需要清理引用
            _database = null;
            _mongoClient = null;
            _isConnected = false;

            _logger.LogInformation("[MONGO_TARGET] Disconnected from MongoDB target");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MONGO_TARGET] Error during disconnect");
        }
    }

    public async Task<DataWriteResult> WriteAsync<T>(
        T data,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DataWriteResult
        {
            Operation = operation,
            DataType = typeof(T)
        };

        try
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("MongoDB data target is not connected");
            }

            var collectionName = GetCollectionNameFromType(typeof(T));
            var collection = _database!.GetCollection<BsonDocument>(collectionName);

            var document = ConvertToBsonDocument(data);

            switch (operation)
            {
                case DataOperation.Insert:
                    await collection.InsertOneAsync(document, null, cancellationToken);
                    result.Success = true;
                    result.Id = document["_id"];
                    break;

                case DataOperation.Update:
                    var filter = Builders<BsonDocument>.Filter.Eq("_id", document["_id"]);
                    var replaceOptions = new ReplaceOptions { IsUpsert = false };
                    var updateResult = await collection.ReplaceOneAsync(filter, document, replaceOptions, cancellationToken);
                    result.Success = updateResult.ModifiedCount > 0;
                    result.Id = document["_id"];
                    break;

                case DataOperation.Upsert:
                    var upsertFilter = Builders<BsonDocument>.Filter.Eq("_id", document["_id"]);
                    var upsertOptions = new ReplaceOptions { IsUpsert = true };
                    var upsertResult = await collection.ReplaceOneAsync(upsertFilter, document, upsertOptions, cancellationToken);
                    result.Success = true;
                    result.Id = document["_id"];
                    result.Metadata["Upserted"] = upsertResult.UpsertedId;
                    break;

                default:
                    throw new ArgumentException($"Unsupported operation: {operation}");
            }

            _statistics.TotalWrites++;
            _statistics.SuccessfulWrites++;
            _statistics.LastWriteTime = DateTime.UtcNow;

            if (!_statistics.OperationCounts.ContainsKey(operation))
            {
                _statistics.OperationCounts[operation] = 0;
            }
            _statistics.OperationCounts[operation]++;

            _logger.LogDebug("[MONGO_TARGET] Successfully wrote {Operation} {DataType} to collection {Collection}",
                operation, typeof(T).Name, collectionName);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;

            _statistics.TotalWrites++;
            _statistics.FailedWrites++;

            _logger.LogError(ex, "[MONGO_TARGET] Failed to write {Operation} {DataType}", operation, typeof(T).Name);
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            // 更新平均写入时间
            if (_statistics.TotalWrites > 0)
            {
                _statistics.AverageWriteTimeMs =
                    (_statistics.AverageWriteTimeMs * (_statistics.TotalWrites - 1) + result.ExecutionTimeMs) / _statistics.TotalWrites;
            }

            // 触发写入事件
            OnWrite?.Invoke(this, new DataWriteEventArgs
            {
                Result = result,
                TargetName = Name
            });
        }

        return result;
    }

    public async Task<BatchDataWriteResult> WriteBatchAsync<T>(
        IEnumerable<T> dataList,
        DataOperation operation = DataOperation.Insert,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<DataWriteResult>();
        var dataListList = dataList.ToList();

        if (dataListList.Count == 0)
        {
            return new BatchDataWriteResult
            {
                TotalCount = 0,
                SuccessCount = 0,
                FailureCount = 0,
                Operation = operation,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }

        try
        {
            var collectionName = GetCollectionNameFromType(typeof(T));
            var collection = _database!.GetCollection<BsonDocument>(collectionName);

            switch (operation)
            {
                case DataOperation.Insert:
                    var documents = dataListList.Select(ConvertToBsonDocument).ToList();
                    await collection.InsertManyAsync(documents, null, cancellationToken);

                    foreach (var doc in documents)
                    {
                        results.Add(new DataWriteResult
                        {
                            Success = true,
                            Operation = operation,
                            DataType = typeof(T),
                            Id = doc["_id"],
                            ExecutionTimeMs = 0
                        });
                    }
                    break;

                default:
                    // 对于非插入操作，逐个处理
                    foreach (var data in dataListList)
                    {
                        var result = await WriteAsync(data, operation, cancellationToken);
                        results.Add(result);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MONGO_TARGET] Batch write operation failed");

            // 如果批量操作失败，尝试逐个操作
            foreach (var data in dataListList)
            {
                var result = await WriteAsync(data, operation, cancellationToken);
                results.Add(result);
            }
        }

        stopwatch.Stop();

        var batchResult = new BatchDataWriteResult
        {
            TotalCount = dataListList.Count,
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success),
            Operation = operation,
            SuccessResults = results.Where(r => r.Success).ToList(),
            FailureResults = results.Where(r => !r.Success).ToList(),
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds
        };

        _logger.LogInformation("[MONGO_TARGET] Batch write completed: {Success}/{Total} successful in {ElapsedMs}ms",
            batchResult.SuccessCount, batchResult.TotalCount, batchResult.ExecutionTimeMs);

        return batchResult;
    }

    public async Task<DataWriteResult> WriteChangeAsync(
        DatabaseChange change,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DataWriteResult
        {
            Operation = MapOperation(change.Operation),
            DataType = typeof(DatabaseChange)
        };

        try
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("MongoDB data target is not connected");
            }

            var collectionName = change.Table;
            var collection = _database!.GetCollection<BsonDocument>(collectionName);

            var document = ConvertDatabaseChangeToBsonDocument(change);

            switch (change.Operation.ToLower())
            {
                case "c":
                case "create":
                case "insert":
                    await collection.InsertOneAsync(document, null, cancellationToken);
                    result.Success = true;
                    break;

                case "u":
                case "update":
                    var filter = Builders<BsonDocument>.Filter.Eq("_id", document["_id"]);
                    var replaceOptions = new ReplaceOptions { IsUpsert = false };
                    var updateResult = await collection.ReplaceOneAsync(filter, document, replaceOptions, cancellationToken);
                    result.Success = updateResult.ModifiedCount > 0;
                    break;

                case "d":
                case "delete":
                    var deleteFilter = Builders<BsonDocument>.Filter.Eq("_id", document["_id"]);
                    var deleteResult = await collection.DeleteOneAsync(deleteFilter, cancellationToken);
                    result.Success = deleteResult.DeletedCount > 0;
                    break;

                default:
                    throw new ArgumentException($"Unsupported operation: {change.Operation}");
            }

            result.Id = document["_id"];
            result.Metadata["Table"] = change.Table;
            result.Metadata["Operation"] = change.Operation;

            _statistics.TotalWrites++;
            _statistics.SuccessfulWrites++;
            _statistics.LastWriteTime = DateTime.UtcNow;

            _logger.LogDebug("[MONGO_TARGET] Successfully applied change {Operation} on {Table}",
                change.Operation, change.Table);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;

            _statistics.TotalWrites++;
            _statistics.FailedWrites++;

            _logger.LogError(ex, "[MONGO_TARGET] Failed to apply change {Operation} on {Table}",
                change.Operation, change.Table);
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            OnWrite?.Invoke(this, new DataWriteEventArgs
            {
                Result = result,
                TargetName = Name
            });
        }

        return result;
    }

    public async Task<BatchDataWriteResult> WriteChangesBatchAsync(
        IEnumerable<DatabaseChange> changes,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<DataWriteResult>();
        var changesList = changes.ToList();

        foreach (var change in changesList)
        {
            var result = await WriteChangeAsync(change, cancellationToken);
            results.Add(result);
        }

        stopwatch.Stop();

        var batchResult = new BatchDataWriteResult
        {
            TotalCount = changesList.Count,
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success),
            Operation = DataOperation.Unknown,
            SuccessResults = results.Where(r => r.Success).ToList(),
            FailureResults = results.Where(r => !r.Success).ToList(),
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds
        };

        _logger.LogInformation("[MONGO_TARGET] Batch change write completed: {Success}/{Total} successful in {ElapsedMs}ms",
            batchResult.SuccessCount, batchResult.TotalCount, batchResult.ExecutionTimeMs);

        return batchResult;
    }

    public async Task<DataWriteResult> DeleteAsync<T>(
        object id,
        CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new DataWriteResult
        {
            Operation = DataOperation.Delete,
            DataType = typeof(T),
            Id = id
        };

        try
        {
            if (!_isConnected)
            {
                throw new InvalidOperationException("MongoDB data target is not connected");
            }

            var collectionName = GetCollectionNameFromType(typeof(T));
            var collection = _database!.GetCollection<BsonDocument>(collectionName);

            var filter = Builders<BsonDocument>.Filter.Eq("_id", ConvertToBsonValue(id));
            var deleteResult = await collection.DeleteOneAsync(filter, cancellationToken);

            result.Success = deleteResult.DeletedCount > 0;

            if (result.Success)
            {
                _statistics.TotalWrites++;
                _statistics.SuccessfulWrites++;
                _statistics.LastWriteTime = DateTime.UtcNow;

                if (!_statistics.OperationCounts.ContainsKey(DataOperation.Delete))
                {
                    _statistics.OperationCounts[DataOperation.Delete] = 0;
                }
                _statistics.OperationCounts[DataOperation.Delete]++;

                _logger.LogDebug("[MONGO_TARGET] Successfully deleted {Type} with ID {Id}", typeof(T).Name, id);
            }
            else
            {
                result.ErrorMessage = "No documents deleted";
                _statistics.TotalWrites++;
                _statistics.FailedWrites++;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;

            _statistics.TotalWrites++;
            _statistics.FailedWrites++;

            _logger.LogError(ex, "[MONGO_TARGET] Failed to delete {Type} with ID {Id}", typeof(T).Name, id);
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            OnWrite?.Invoke(this, new DataWriteEventArgs
            {
                Result = result,
                TargetName = Name
            });
        }

        return result;
    }

    public async Task<DataTargetHealth> CheckHealthAsync()
    {
        var health = new DataTargetHealth
        {
            LastCheck = DateTime.UtcNow
        };

        try
        {
            if (!_isConnected)
            {
                health.IsHealthy = false;
                health.Status = "Disconnected";
                health.Message = "MongoDB connection is not established";
                return health;
            }

            // 执行ping命令测试连接
            var pingResult = await _database!.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1));

            if (pingResult.Contains("ok") && pingResult["ok"].AsBoolean)
            {
                health.IsHealthy = true;
                health.Status = "Connected";
                health.Message = "MongoDB target connection is healthy";

                // 获取数据库统计信息
                var stats = await _database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("dbStats", 1));

                health.Metrics.Add("DatabaseName", _databaseName);
                health.Metrics.Add("Collections", stats.Contains("collections") ? stats["collections"] : 0);
                health.Metrics.Add("DataSize", stats.Contains("dataSize") ? stats["dataSize"] : 0);

                // 添加同步统计信息
                health.Metrics.Add("TotalWrites", _statistics.TotalWrites);
                health.Metrics.Add("SuccessRate", _statistics.SuccessRate);
                health.Metrics.Add("AverageWriteTimeMs", _statistics.AverageWriteTimeMs);
                health.Metrics.Add("LastWriteTime", _statistics.LastWriteTime?.ToString() ?? "Never");
            }
            else
            {
                health.IsHealthy = false;
                health.Status = "Error";
                health.Message = "MongoDB ping command failed";
            }
        }
        catch (Exception ex)
        {
            health.IsHealthy = false;
            health.Status = "Unhealthy";
            health.Message = ex.Message;
            _logger.LogError(ex, "[MONGO_TARGET] Health check failed");
        }

        return await Task.FromResult(health);
    }

    public async Task<DataTargetStatistics> GetStatisticsAsync()
    {
        return await Task.FromResult(_statistics);
    }

    public async Task ResetStatisticsAsync()
    {
        _statistics.Reset();
        _logger.LogInformation("[MONGO_TARGET] Statistics reset");
        await Task.CompletedTask;
    }

    private BsonDocument ConvertToBsonDocument<T>(T data)
    {
        if (data is BsonDocument bsonDoc)
        {
            return bsonDoc;
        }

        if (data is Dictionary<string, object> dict)
        {
            var doc = new BsonDocument();
            foreach (var kvp in dict)
            {
                doc[kvp.Key] = ConvertToBsonValue(kvp.Value);
            }
            return doc;
        }

        // 使用JSON序列化将对象转换为BsonDocument
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        return BsonDocument.Parse(json);
    }

    private BsonDocument ConvertDatabaseChangeToBsonDocument(DatabaseChange change)
    {
        var doc = new BsonDocument
        {
            ["_id"] = change.Lsn ?? 0,
            ["operation"] = change.Operation,
            ["database"] = change.Database,
            ["schema"] = change.Schema,
            ["table"] = change.Table,
            ["timestamp"] = change.Timestamp,
            ["transactionId"] = change.TransactionId == null ? BsonNull.Value : new BsonString(change.TransactionId)
        };

        if (change.Before != null)
        {
            doc["before"] = ConvertDictionaryToBsonDocument(change.Before);
        }

        if (change.After != null)
        {
            doc["after"] = ConvertDictionaryToBsonDocument(change.After);
        }

        if (change.Source.Any())
        {
            doc["source"] = ConvertDictionaryToBsonDocument(change.Source);
        }

        if (change.Metadata.Any())
        {
            doc["metadata"] = ConvertDictionaryToBsonDocument(change.Metadata);
        }

        return doc;
    }

    private BsonDocument ConvertDictionaryToBsonDocument(Dictionary<string, object> dictionary)
    {
        var doc = new BsonDocument();
        foreach (var kvp in dictionary)
        {
            doc[kvp.Key] = ConvertToBsonValue(kvp.Value);
        }
        return doc;
    }

    private BsonValue ConvertToBsonValue(object value)
    {
        return value switch
        {
            null => BsonNull.Value,
            string str => str,
            int i => i,
            long l => l,
            double d => d,
            decimal dec => (double)dec,
            bool b => b,
            DateTime dt => dt,
            Guid guid => guid.ToString(),
            BsonValue bson => bson,
            Dictionary<string, object> dict => ConvertDictionaryToBsonDocument(dict),
            _ => value.ToString()
        };
    }

    private string GetCollectionNameFromType(Type type)
    {
        var typeName = type.Name.ToLower();
        return typeName switch
        {
            var name when name.Contains("product") => "products",
            var name when name.Contains("order") => "orders",
            var name when name.Contains("category") => "categories",
            var name when name.Contains("databasechange") => "database_changes",
            _ => typeName + "s"
        };
    }

    private DataOperation MapOperation(string operation)
    {
        return operation.ToLower() switch
        {
            "c" or "create" or "insert" => DataOperation.Insert,
            "u" or "update" => DataOperation.Update,
            "d" or "delete" => DataOperation.Delete,
            _ => DataOperation.Unknown
        };
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
    }
}