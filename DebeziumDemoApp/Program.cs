using DebeziumDemoApp.Services;
using DebeziumDemoApp.Extensions;
using DebeziumDemoApp.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddOpenApi();

// Register core services (only essential services for RabbitMQ + Debezium Server architecture)
builder.Services.AddSingleton<IBackupPostgresService, BackupPostgresService>();

// Add Universal Data Sync Service
builder.Services.AddUniversalSyncFromConfiguration(builder.Configuration);

// Add hosted service for backup database initialization only
builder.Services.AddHostedService<BackupDatabaseInitializer>();

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

// Enable static files
app.UseStaticFiles();

// Disable HTTPS redirection for development
// app.UseHttpsRedirection();
app.UseCors("AllowAll");

// Map universal sync endpoints
app.MapUniversalSyncEndpoints();

// Map UI API endpoints
app.MapUiApiEndpoints();

// Map monitoring API endpoints
app.MapMonitoringApiEndpoints();

// Map basic health endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "Debezium Universal Data Sync"
}))
.WithName("HealthCheck")
.WithTags("Health");

// Default route - serve the HTML interface
app.MapGet("/", () => Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"), "text/html"))
.WithName("Home")
.WithTags("Info");

// API info endpoint
app.MapGet("/api/info", () => Results.Ok(new
{
    message = "Debezium Universal Data Sync Service",
    version = "1.0.0",
    status = "running",
    endpoints = new
    {
        health = "/health",
        universalSync = "/api/universal-sync/status",
        docs = "/swagger",
        ui = "/"
    }
}))
.WithName("ApiInfo")
.WithTags("Info");

app.Run();