using System.Collections.Concurrent;

namespace DebeziumDemoApp.Services;

public interface ICDCMetricsService
{
    void RecordChange(string operation, string table);
    CDMMetrics GetCurrentMetrics();
    void ResetMetrics();
}

public class CDMMetricsService : ICDCMetricsService
{
    private readonly ConcurrentDictionary<string, int> _operationCounts = new();
    private readonly ConcurrentDictionary<string, int> _tableCounts = new();
    private DateTime _lastChangeTime = DateTime.UtcNow;
    private long _totalChanges = 0;

    public CDMMetricsService()
    {
        // Initialize operation counters
        _operationCounts.TryAdd("INSERT", 0);
        _operationCounts.TryAdd("UPDATE", 0);
        _operationCounts.TryAdd("DELETE", 0);
        _operationCounts.TryAdd("READ", 0);
    }

    public void RecordChange(string operation, string table)
    {
        _operationCounts.AddOrUpdate(operation.ToUpper(), 1, (key, value) => value + 1);
        _tableCounts.AddOrUpdate(table.ToLower(), 1, (key, value) => value + 1);
        _lastChangeTime = DateTime.UtcNow;
        Interlocked.Increment(ref _totalChanges);
    }

    public CDMMetrics GetCurrentMetrics()
    {
        return new CDMMetrics
        {
            TotalChanges = _totalChanges,
            LastChangeTime = _lastChangeTime,
            OperationCounts = new Dictionary<string, int>(_operationCounts),
            TableCounts = new Dictionary<string, int>(_tableCounts)
        };
    }

    public void ResetMetrics()
    {
        foreach (var key in _operationCounts.Keys)
        {
            _operationCounts[key] = 0;
        }
        foreach (var key in _tableCounts.Keys)
        {
            _tableCounts[key] = 0;
        }
        _totalChanges = 0;
        _lastChangeTime = DateTime.UtcNow;
    }
}

public class CDMMetrics
{
    public long TotalChanges { get; set; }
    public DateTime LastChangeTime { get; set; }
    public Dictionary<string, int> OperationCounts { get; set; } = new();
    public Dictionary<string, int> TableCounts { get; set; } = new();
}