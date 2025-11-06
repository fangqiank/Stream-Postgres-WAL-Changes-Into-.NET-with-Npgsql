using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// Real-time notification service for broadcasting CDC events to connected clients
    /// </summary>
    public class RealTimeNotificationService : IDisposable
    {
        private readonly ConcurrentDictionary<string, WebSocket> _connectedClients = new();
        private readonly ILogger<RealTimeNotificationService> _logger;
        private readonly Timer? _cleanupTimer;

        public RealTimeNotificationService(ILogger<RealTimeNotificationService> logger)
        {
            _logger = logger;

            // Start cleanup timer to remove dead connections
            _cleanupTimer = new Timer(CleanupDeadConnections, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Add a new WebSocket client connection
        /// </summary>
        public string AddClient(WebSocket webSocket)
        {
            var clientId = Guid.NewGuid().ToString();
            _connectedClients.TryAdd(clientId, webSocket);

            _logger.LogInformation("WebSocket client connected: {ClientId}, Total clients: {TotalClients}",
                clientId, _connectedClients.Count);

            return clientId;
        }

        /// <summary>
        /// Remove a WebSocket client connection
        /// </summary>
        public void RemoveClient(string clientId)
        {
            if (_connectedClients.TryRemove(clientId, out var webSocket))
            {
                _logger.LogInformation("WebSocket client disconnected: {ClientId}, Total clients: {TotalClients}",
                    clientId, _connectedClients.Count);

                try
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected",
                            CancellationToken.None).Wait();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing WebSocket for client: {ClientId}", clientId);
                }
            }
        }

        /// <summary>
        /// Broadcast order status change to all connected clients
        /// </summary>
        public async Task BroadcastOrderStatusChangeAsync(Guid orderId, string oldStatus, string newStatus)
        {
            var notification = new
            {
                Type = "OrderStatusChanged",
                Timestamp = DateTime.UtcNow,
                Data = new
                {
                    OrderId = orderId,
                    OldStatus = oldStatus,
                    NewStatus = newStatus
                }
            };

            await BroadcastAsync(notification);
        }

        /// <summary>
        /// Broadcast new order creation to all connected clients
        /// </summary>
        public async Task BroadcastOrderCreatedAsync(Order order)
        {
            var notification = new
            {
                Type = "OrderCreated",
                Timestamp = DateTime.UtcNow,
                Data = new
                {
                    OrderId = order.Id,
                    CustomerName = order.CustomerName,
                    Amount = order.Amount,
                    Status = order.Status,
                    CreatedAt = order.CreatedAt
                }
            };

            await BroadcastAsync(notification);
        }

        /// <summary>
        /// Broadcast order update to all connected clients
        /// </summary>
        public async Task BroadcastOrderUpdatedAsync(Order order, string[] changedFields)
        {
            var notification = new
            {
                Type = "OrderUpdated",
                Timestamp = DateTime.UtcNow,
                Data = new
                {
                    OrderId = order.Id,
                    ChangedFields = changedFields,
                    CustomerName = order.CustomerName,
                    Amount = order.Amount,
                    Status = order.Status,
                    UpdatedAt = order.UpdatedAt
                }
            };

            await BroadcastAsync(notification);
        }

        /// <summary>
        /// Broadcast generic CDC event to all connected clients
        /// </summary>
        public async Task BroadcastCdcEventAsync(string eventType, string tableName, object eventData)
        {
            var notification = new
            {
                Type = "CdcEvent",
                Timestamp = DateTime.UtcNow,
                Data = new
                {
                    EventType = eventType,
                    TableName = tableName,
                    EventData = eventData
                }
            };

            await BroadcastAsync(notification);
        }

        /// <summary>
        /// Get count of connected clients
        /// </summary>
        public int GetConnectedClientCount()
        {
            return _connectedClients.Count;
        }

        /// <summary>
        /// Broadcast message to all connected WebSocket clients
        /// </summary>
        private async Task BroadcastAsync(object message)
        {
            if (_connectedClients.IsEmpty)
            {
                _logger.LogDebug("No connected clients to broadcast message to");
                return;
            }

            var jsonMessage = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(jsonMessage);

            var deadClients = new List<string>();

            foreach (var clientPair in _connectedClients)
            {
                var clientId = clientPair.Key;
                var webSocket = clientPair.Value;

                try
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.SendAsync(
                            new ArraySegment<byte>(buffer),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);
                    }
                    else
                    {
                        deadClients.Add(clientId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending message to WebSocket client: {ClientId}", clientId);
                    deadClients.Add(clientId);
                }
            }

            // Remove dead connections
            foreach (var deadClientId in deadClients)
            {
                RemoveClient(deadClientId);
            }

            _logger.LogDebug("Broadcast message to {ClientCount} clients",
                _connectedClients.Count - deadClients.Count);
        }

        /// <summary>
        /// Cleanup dead WebSocket connections
        /// </summary>
        private void CleanupDeadConnections(object? state)
        {
            var deadClients = new List<string>();

            foreach (var clientPair in _connectedClients)
            {
                var clientId = clientPair.Key;
                var webSocket = clientPair.Value;

                if (webSocket.State != WebSocketState.Open)
                {
                    deadClients.Add(clientId);
                }
            }

            foreach (var deadClientId in deadClients)
            {
                _connectedClients.TryRemove(deadClientId, out _);
            }

            if (deadClients.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} dead WebSocket connections", deadClients.Count);
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();

            foreach (var clientPair in _connectedClients)
            {
                try
                {
                    if (clientPair.Value.State == WebSocketState.Open)
                    {
                        clientPair.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service shutting down",
                            CancellationToken.None).Wait();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing WebSocket during disposal");
                }
            }

            _connectedClients.Clear();
        }
    }
}