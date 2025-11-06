using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;
using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Database configuration
builder.Services.AddDbContext<AppDbContext>(
    options =>
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
            npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: builder.Configuration.GetValue<int>("Database:MaxRetryCount", 5),
                    maxRetryDelay: TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("Database:MaxRetryDelay", 30)),
                    errorCodesToAdd: null);
                npgsqlOptions.CommandTimeout(builder.Configuration.GetValue<int>("Database:CommandTimeout", 30));
            });
        options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
    });

// Authentication configuration
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
           OnMessageReceived = context =>
           {
               var accessToken = context.Request.Query["access_token"];
               var path = context.HttpContext.Request.Path;

               if (!string.IsNullOrEmpty(accessToken) &&
                   (path.StartsWithSegments("/cdc-ws")))
               {
                   context.Token = accessToken;
               }

               return Task.CompletedTask;
           },
           OnAuthenticationFailed = context =>
           {
               context.Response.StatusCode = 401;
               context.Response.ContentType = "application/json";
               return context.Response.WriteAsJsonAsync(new { error = "Authentication failed", message = context.Exception.Message });
           }
        };
    });

builder.Services.AddAuthorization();

// Configure console encoding to handle UTF-8 properly
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch
{
    // Fallback for systems that don't support UTF-8 console encoding
    Console.WriteLine("Warning: Could not set console encoding to UTF-8");
}

// Configure logical replication options
builder.Services.Configure<LogicalReplicationOptions>(
    builder.Configuration.GetSection("Replication"));

// Configure CDC options
builder.Services.Configure<CdcOptions>(builder.Configuration.GetSection("Cdc"));

// Register services
builder.Services.AddSingleton<ICdcService, CdcService>();
builder.Services.AddSingleton<CdcEventHandlerManager>();
builder.Services.AddTransient<ICdcEventHandler, OrderChangeEventHandler>();
builder.Services.AddTransient<ICdcEventHandler, OutboxEventHandler>();
builder.Services.AddTransient<ICdcEventHandler, GenericChangeEventHandler>();
builder.Services.AddSingleton<IReplicationHealthMonitor, ReplicationHealthMonitor>();
builder.Services.AddSingleton<IReplicationEventProcessor, ReplicationEventProcessor>();
builder.Services.AddSingleton<ReplicationSlotManagerService>();
builder.Services.AddSingleton<RealTimeNotificationService>();

// Register hosted services
builder.Services.AddHostedService<CdcInitializer>();
builder.Services.AddHostedService<LogicalReplicationService>();
builder.Services.AddHostedService<OutboxEventProcessor>();

// Add memory cache
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1024 * 1024 * 100; // 100MB cache limit
    options.CompactionPercentage = 0.2;
});

// Add additional CORS policy
builder.Services.AddCors(opts =>
{
    opts.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000", "https://localhost:3000"])
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure middleware pipeline
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Add WebSocket middleware
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(builder.Configuration.GetValue<int>("WebSocket:KeepAliveInterval", 30))
});

// Map endpoints using extension classes
// Public endpoints (no authentication required)
app.MapPublicEndpoints();

// Health endpoints (no authentication required)
app.MapHealthEndpoints();

// Authentication endpoints (no authentication required)
app.MapAuthenticationEndpoints(builder.Configuration);

// WebSocket endpoints (mixed authentication requirements)
app.MapWebSocketEndpoints();

// Order endpoints (authentication required)
app.MapOrderEndpoints();

// Replication endpoints (authentication required)
app.MapReplicationEndpoints();

app.Run();
