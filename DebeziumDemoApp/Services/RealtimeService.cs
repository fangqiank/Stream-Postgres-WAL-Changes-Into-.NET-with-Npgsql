using DebeziumDemoApp.Models;

namespace DebeziumDemoApp.Services;

public interface IRealtimeService
{
    void RegisterClient(Guid clientId, IResponseStreamWriter response);
    void UnregisterClient(Guid clientId);
    Task BroadcastChangeAsync(DatabaseChangeNotification change);
    int ConnectedClientsCount { get; }
}

public class RealtimeService : IRealtimeService
{
    private readonly ILogger<RealtimeService> _logger;
    private readonly Dictionary<Guid, IResponseStreamWriter> _clients = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public RealtimeService(ILogger<RealtimeService> logger)
    {
        _logger = logger;
    }

    public int ConnectedClientsCount => _clients.Count;

    public void RegisterClient(Guid clientId, IResponseStreamWriter response)
    {
        _semaphore.Wait();
        try
        {
            _clients[clientId] = response;
            _logger.LogInformation("Client {ClientId} connected. Total clients: {Count}", clientId, _clients.Count);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void UnregisterClient(Guid clientId)
    {
        _semaphore.Wait();
        try
        {
            if (_clients.Remove(clientId))
            {
                _logger.LogInformation("Client {ClientId} disconnected. Total clients: {Count}", clientId, _clients.Count);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task BroadcastChangeAsync(DatabaseChangeNotification change)
    {
        var clientsToRemove = new List<Guid>();
        var connectedClients = new List<(Guid ClientId, IResponseStreamWriter Response)>();

        await _semaphore.WaitAsync();
        try
        {
            connectedClients = _clients.Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }
        finally
        {
            _semaphore.Release();
        }

        var changeJson = System.Text.Json.JsonSerializer.Serialize(change, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        foreach (var (clientId, response) in connectedClients)
        {
            try
            {
                await response.WriteAsync($"data: {changeJson}\n\n");
                await response.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending data to client {ClientId}", clientId);
                clientsToRemove.Add(clientId);
            }
        }

        // Remove disconnected clients
        if (clientsToRemove.Count > 0)
        {
            await _semaphore.WaitAsync();
            try
            {
                foreach (var clientId in clientsToRemove)
                {
                    _clients.Remove(clientId);
                }
                _logger.LogInformation("Removed {Count} disconnected clients. Total clients: {Total}",
                    clientsToRemove.Count, _clients.Count);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}

public interface IResponseStreamWriter
{
    Task WriteAsync(string data);
    Task FlushAsync();
}