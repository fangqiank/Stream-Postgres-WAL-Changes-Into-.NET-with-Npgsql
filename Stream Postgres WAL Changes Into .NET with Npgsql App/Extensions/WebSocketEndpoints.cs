using Microsoft.EntityFrameworkCore;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;
using System.Text.Json;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;

public static class WebSocketEndpoints
{
    public static void MapWebSocketEndpoints(this WebApplication app)
    {
        // WebSocket endpoint for real-time notifications (no auth required for basic functionality)
        app.MapGet("/api/realtime", async (HttpContext context, RealTimeNotificationService notificationService, ILogger<Program> logger) =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var clientId = notificationService.AddClient(webSocket);

                try
                {
                    // Send welcome message
                    var welcomeMessage = new
                    {
                        Type = "Connected",
                        Message = "Real-time CDC notifications connected successfully",
                        ClientId = clientId,
                        Timestamp = DateTime.UtcNow
                    };

                    var welcomeJson = JsonSerializer.Serialize(welcomeMessage);
                    var buffer = System.Text.Encoding.UTF8.GetBytes(welcomeJson);
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(buffer),
                        System.Net.WebSockets.WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);

                    logger.LogInformation("Real-time WebSocket client connected: {ClientId}", clientId);

                    // Keep connection alive and listen for client messages
                    var receiveBuffer = new byte[1024 * 4];
                    while (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        var result = await webSocket.ReceiveAsync(
                            new ArraySegment<byte>(receiveBuffer),
                            CancellationToken.None);

                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        {
                            logger.LogInformation("WebSocket client requested close: {ClientId}", clientId);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "WebSocket connection error for client: {ClientId}", clientId);
                }
                finally
                {
                    notificationService.RemoveClient(clientId);
                }

                return Results.Ok();
            }
            else
            {
                return Results.BadRequest("Expected WebSocket request");
            }
        })
        .WithName("RealTimeNotifications")
        .WithSummary("Real-time CDC Notifications")
        .WithDescription("WebSocket endpoint for real-time CDC notifications")
        .WithTags("WebSocket");

        // Alternative WebSocket endpoint with simpler implementation
        app.MapGet("/cdc-ws", async (HttpContext context, AppDbContext db) =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                await HandleWebSocketConnection(webSocket, db);
            }
            else
            {
                context.Response.StatusCode = 400;
            }
        })
        .WithName("CdcWebSocket")
        .WithSummary("CDC WebSocket Endpoint")
        .WithDescription("Alternative WebSocket endpoint for CDC events")
        .WithTags("WebSocket");
    }

    private static async Task HandleWebSocketConnection(System.Net.WebSockets.WebSocket webSocket, AppDbContext db)
    {
        var buffer = new byte[1024 * 4];

        try
        {
            var welcomeMessage = new
            {
                type = "connection",
                message = "CDC WebSocket connected successfully",
                timestamp = DateTime.UtcNow
            };

            await SendWebSocketMessage(webSocket, welcomeMessage);

            while (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        "Closing connection",
                        CancellationToken.None);
                    break;
                }

                await SimulateRealtimeEvents(webSocket, db);
                await Task.Delay(2000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebSocket connection error: {ex.Message}");
        }
    }

    private static async Task SendWebSocketMessage(System.Net.WebSockets.WebSocket webSocket, object message)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            System.Net.WebSockets.WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private static async Task SimulateRealtimeEvents(System.Net.WebSockets.WebSocket webSocket, AppDbContext db)
    {
        var unprocessedEvents = await db.OutboxEvents
            .Where(oe => !oe.Processed)
            .OrderBy(oe => oe.CreatedAt)
            .Take(10)
            .ToListAsync();

        foreach (var outboxEvent in unprocessedEvents)
        {
            var eventMessage = new
            {
                type = "cdc_event",
                eventId = outboxEvent.Id,
                eventType = outboxEvent.EventType,
                aggregateType = outboxEvent.AggregateType,
                aggregateId = outboxEvent.AggregateId,
                payload = JsonSerializer.Deserialize<object>(outboxEvent.Payload),
                timestamp = outboxEvent.CreatedAt
            };

            await SendWebSocketMessage(webSocket, eventMessage);

            outboxEvent.Processed = true;
            outboxEvent.ProcessedAt = DateTime.UtcNow;
        }

        if (unprocessedEvents.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }
}