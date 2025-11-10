using DebeziumDemoApp.Models;
using DebeziumDemoApp.Services;
using DebeziumDemoApp.Extensions;
using Npgsql;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddOpenApi();

// Register custom services
builder.Services.AddSingleton<INeonPostgresService, NeonPostgresService>();
builder.Services.AddSingleton<IBackupPostgresService, BackupPostgresService>();
builder.Services.AddSingleton<IDataSyncService, DataSyncService>();
builder.Services.AddSingleton<IKafkaService, KafkaService>();
builder.Services.AddSingleton<IRealtimeService, RealtimeService>();
builder.Services.AddSingleton<ICDCMetricsService, CDMMetricsService>();

// Add hosted service for Kafka (CDC)
builder.Services.AddHostedService<KafkaHostedService>();

// Add hosted service for backup database initialization
builder.Services.AddHostedService<BackupDatabaseInitializer>(provider =>
{
    var backupDb = provider.GetRequiredService<IBackupPostgresService>();
    var dataSyncService = provider.GetRequiredService<IDataSyncService>();
    var logger = provider.GetRequiredService<ILogger<BackupDatabaseInitializer>>();

    return new BackupDatabaseInitializer(backupDb, dataSyncService, logger);
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAll");

// Add explicit route for root to serve index.html
app.MapGet("/", async (HttpContext context) =>
{
    context.Response.ContentType = "text/html";
    var indexPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "index.html");
    await context.Response.SendFileAsync(indexPath);
});

// Map organized endpoints using extension classes
app.MapHealthEndpoints();
app.MapMonitoringEndpoints();
app.MapBackupEndpoints();
app.MapProductsEndpoints();
app.MapOrdersEndpoints();
app.MapCategoriesEndpoints();

// Server-Sent Events endpoint for real-time updates
app.MapGet("/api/changes/stream", async (HttpContext context, IRealtimeService realtimeService) =>
{
    context.Response.Headers["Content-Type"] = "text/event-stream";
    context.Response.Headers["Cache-Control"] = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";

    var clientId = Guid.NewGuid();
    var response = context.Response;
    var token = context.RequestAborted;

    // Create a response stream wrapper
    var responseWriter = new HttpResponseStreamWriter(response);

    // Register this client for real-time updates
    realtimeService.RegisterClient(clientId, responseWriter);

    try
    {
        // Send initial connection message
        await response.WriteAsync($"data: {{\"type\":\"connected\",\"clientId\":\"{clientId}\"}}\n\n");
        await response.Body.FlushAsync();

        // Keep the connection alive and send periodic heartbeats
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(30000, token); // Send heartbeat every 30 seconds
            if (!token.IsCancellationRequested)
            {
                await response.WriteAsync($"data: {{\"type\":\"heartbeat\",\"timestamp\":\"{DateTime.UtcNow:O}\"}}\n\n");
                await response.Body.FlushAsync();
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected normally
    }
    catch (Exception ex)
    {
        // Log any other exceptions
        Console.WriteLine($"Error in SSE stream: {ex.Message}");
    }
    finally
    {
        // Unregister the client when the connection ends
        realtimeService.UnregisterClient(clientId);
    }
})
.WithName("StreamChanges");

app.Run();