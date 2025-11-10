using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// 演示如何读取 User Secrets 中的配置值
    /// </summary>
    public class UserSecretsExample
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserSecretsExample> _logger;

        public UserSecretsExample(IConfiguration configuration, ILogger<UserSecretsExample> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// 演示不同的配置读取方法
        /// </summary>
        public void DemonstrateConfigurationReading()
        {
            _logger.LogInformation("=== User Secrets 读取示例 ===");

            // 方法1: 读取连接字符串
            var defaultConnection = _configuration.GetConnectionString("DefaultConnection");
            var localConnection = _configuration.GetConnectionString("LocalConnection");

            _logger.LogInformation("DefaultConnection: {Connection}",
                string.IsNullOrEmpty(defaultConnection) ? "[空 - 将从User Secrets读取]" : MaskConnectionString(defaultConnection));
            _logger.LogInformation("LocalConnection: {Connection}", MaskConnectionString(localConnection ?? string.Empty));

            // 方法2: 读取嵌套配置值
            var jwtSecret = _configuration["Jwt:Secret"];
            var jwtIssuer = _configuration["Jwt:Issuer"];
            var jwtAudience = _configuration["Jwt:Audience"];

            _logger.LogInformation("JWT Secret: {Secret}",
                string.IsNullOrEmpty(jwtSecret) ? "[空 - 将从User Secrets读取]" : "***已配置***");
            _logger.LogInformation("JWT Issuer: {Issuer}", jwtIssuer ?? "[空 - 将从User Secrets读取]");
            _logger.LogInformation("JWT Audience: {Audience}", jwtAudience ?? "[空 - 将从User Secrets读取]");

            // 方法3: 使用 GetValue<T>() 方法读取并设置默认值
            var maxRetryCount = _configuration.GetValue("Database:MaxRetryCount", 5);
            var commandTimeout = _configuration.GetValue("Database:CommandTimeout", 30);

            _logger.LogInformation("Database MaxRetryCount: {Value}", maxRetryCount);
            _logger.LogInformation("Database CommandTimeout: {Value}", commandTimeout);

            // 方法4: 读取整个配置节
            var jwtSection = _configuration.GetSection("Jwt");
            var replicationSection = _configuration.GetSection("Replication");

            _logger.LogInformation("JWT Section exists: {Exists}", jwtSection.Exists());
            _logger.LogInformation("Replication Section exists: {Exists}", replicationSection.Exists());
            _logger.LogInformation("Replication SlotName: {SlotName}", replicationSection["SlotName"]);

            // 方法5: 绑定到强类型对象
            var jwtOptions = new JwtOptions();
            _configuration.GetSection("Jwt").Bind(jwtOptions);

            _logger.LogInformation("强类型 JWT Options - Secret configured: {Configured}",
                !string.IsNullOrEmpty(jwtOptions.Secret));
            _logger.LogInformation("强类型 JWT Options - Issuer: {Issuer}", jwtOptions.Issuer);
        }

        /// <summary>
        /// 隐藏连接字符串中的敏感信息
        /// </summary>
        private string MaskConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return "[空]";

            // 隐藏密码部分
            var parts = connectionString.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Trim().StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "Password=***";
                }
                else if (parts[i].Trim().StartsWith("User Id=", StringComparison.OrdinalIgnoreCase) ||
                         parts[i].Trim().StartsWith("Username=", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = parts[i] + "***";
                }
            }

            return string.Join("; ", parts);
        }
    }

    /// <summary>
    /// JWT配置选项类
    /// </summary>
    public class JwtOptions
    {
        public string Secret { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
    }

    /// <summary>
    /// 扩展方法用于注册示例服务
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddUserSecretsExample(this IServiceCollection services)
        {
            services.AddScoped<UserSecretsExample>();
            return services;
        }
    }
}