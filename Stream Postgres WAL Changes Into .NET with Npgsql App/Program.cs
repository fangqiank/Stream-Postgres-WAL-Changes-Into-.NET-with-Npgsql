using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Extensions;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services;
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

// Local database configuration for UI and replicated data
builder.Services.AddDbContext<LocalDbContext>(
    options =>
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("LocalConnection"),
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

// Configure PostgreSQL Logical Replication options
builder.Services.Configure<LogicalReplicationServiceOptions>(builder.Configuration.GetSection("LogicalReplication"));

// Configure Monitoring options
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection("Monitoring"));

// Register services
builder.Services.AddSingleton<IReplicationHealthMonitor, ReplicationHealthMonitor>();
builder.Services.AddSingleton<ReplicationSlotManagerService>();
builder.Services.AddSingleton<RealTimeNotificationService>();
builder.Services.AddUserSecretsExample(); // 添加配置示例服务
// ===== PostgreSQL逻辑复制服务注册 =====
// 注册PostgreSQL逻辑复制服务
if (builder.Configuration.GetValue<bool>("LogicalReplication:Enabled", true))
{
    builder.Services.AddSingleton<PostgreSqlLogicalReplicationService>();
    builder.Services.AddHostedService<PostgreSqlLogicalReplicationService>();
}


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

// Ensure databases are created on startup
using (var scope = app.Services.CreateScope())
{
    var neonContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var localContext = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

    try
    {
        // Create Neon database schema
        neonContext.Database.EnsureCreated();
        Console.WriteLine("✅ Neon database schema verified");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Neon database schema check failed: {ex.Message}");
    }

    try
    {
        // Create local database schema
        localContext.Database.EnsureCreated();
        Console.WriteLine("✅ Local database schema created");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Local database schema check failed: {ex.Message}");
    }
}

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

// Public diagnostic endpoints (no authentication required)
app.MapPublicDiagnosticEndpoints();

// Authentication endpoints (no authentication required)
app.MapAuthenticationEndpoints(builder.Configuration);

// WebSocket endpoints (mixed authentication requirements)
app.MapWebSocketEndpoints();

// Local Order endpoints (authentication required) - Use local database for UI
app.MapLocalOrderEndpoints();

// Order endpoints (authentication required) - Use original database for CDC operations
app.MapOrderEndpoints();

// Replication endpoints (authentication required)
app.MapReplicationEndpoints();

// ===== PostgreSQL逻辑复制端点 =====
// 映射逻辑复制API端点
app.MapLogicalReplicationEndpoints();

app.Run();
