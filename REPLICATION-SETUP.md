# Neon到本地PostgreSQL复制配置指南

## 概述

这个指南将帮助您配置从Neon PostgreSQL数据库到本地PostgreSQL的逻辑复制，实现数据的实时同步。

## 前提条件

1. 本地安装PostgreSQL 13+ (推荐使用PostgreSQL 15)
2. 拥有Neon数据库的连接信息
3. 具有数据库管理员权限

## 配置步骤

### 1. 配置本地PostgreSQL

#### 1.1 修改postgresql.conf

找到本地PostgreSQL的`postgresql.conf`文件，确保以下配置已启用：

```ini
# 启用WAL逻辑复制
wal_level = logical

# 复制槽数量
max_replication_slots = 10

# WAL发送进程数量
max_wal_senders = 10

# 启用WAL归档（可选）
archive_mode = on
archive_command = 'cp %p /var/lib/postgresql/wal_archive/%f'
```

#### 1.2 重启PostgreSQL服务

```bash
# Windows
net stop postgresql
net start postgresql

# Linux/macOS
sudo systemctl restart postgresql
# 或
sudo service postgresql restart
```

#### 1.3 创建复制用户和数据库

执行`scripts/setup-local-replication.sql`：

```bash
psql -U postgres -d your_local_db -f scripts/setup-local-replication.sql
```

### 2. 配置Neon数据库

#### 2.1 创建发布

在Neon数据库中执行以下命令：

```sql
-- 创建发布
CREATE PUBLICATION cdc_publication FOR TABLE orders, outbox_events;

-- 验证发布
SELECT * FROM pg_publication;
```

#### 2.2 创建复制槽

```sql
-- 为Neon到本地复制创建槽
SELECT pg_create_logical_replication_slot('neon_to_local_slot', 'pgoutput');
```

### 3. 创建订阅连接

#### 3.1 获取Neon连接信息

从Neon控制台获取以下信息：
- 主机名 (Hostname)
- 端口 (Port, 通常是5432)
- 数据库名 (Database)
- 用户名 (Username)
- 密码 (Password)

#### 3.2 在本地创建订阅

在本地PostgreSQL中执行：

```sql
CREATE SUBSCRIPTION neon_to_local_subscription
CONNECTION 'host=ep-xxx-xxx.us-east-2.aws.neon.tech user=your_username password=your_password dbname=neondb port=5432 sslmode=require'
PUBLICATION cdc_publication
WITH (slot_name = neon_to_local_slot, create_slot = false, copy_data = true, synchronized_commit = off);
```

#### 3.3 验证订阅状态

```sql
-- 检查订阅状态
SELECT * FROM pg_subscription;

-- 监控复制延迟
SELECT subname, srapplydelay, srflushlsn, srlsn
FROM pg_stat_subscription;
```

## 应用程序配置

### 1. 更新appsettings.json

添加本地数据库连接字符串���

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=ep-xxx-xxx.us-east-2.aws.neon.tech;Database=neondb;Username=your_username;Password=your_password;SSL Mode=Require;Trust Server Certificate=true",
    "LocalConnection": "Host=localhost;Database=your_local_db;Username=postgres;Password=local_password"
  },
  "Replication": {
    "EnableLocalReplication": true,
    "LocalConnectionStringName": "LocalConnection"
  }
}
```

### 2. 创建本地复制监听服务

可以在应用程序中添加一个服务来监控本地数据库的变更：

```csharp
public class LocalReplicationMonitor : BackgroundService
{
    private readonly IDbContextFactory<LocalDbContext> _dbContextFactory;
    private readonly ILogger<LocalReplicationMonitor> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var context = _dbContextFactory.CreateDbContext();
                var recentChanges = await context.Orders
                    .Where(o => o.CreatedAt > DateTime.UtcNow.AddMinutes(-1))
                    .CountAsync();

                if (recentChanges > 0)
                {
                    _logger.LogInformation($"Local replication: {recentChanges} recent changes detected");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring local replication");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
```

## 测试复制功能

### 1. 在Neon中创建测试数据

```sql
INSERT INTO orders (customername, amount, status)
VALUES ('Test Customer', 100.00, 'Pending');
```

### 2. 验证数据同步

在本地数据库中检查：

```sql
SELECT * FROM orders ORDER BY created_at DESC LIMIT 5;
SELECT * FROM outbox_events ORDER BY created_at DESC LIMIT 5;
```

### 3. 监控复制延迟

```sql
SELECT
    subname,
    EXTRACT(EPOCH FROM (now() - srflushlsn)) as replication_lag_seconds
FROM pg_stat_subscription;
```

## 故障排除

### 常见问题

1. **连接失败**: 检查网络连接和防火墙设置
2. **权限错误**: 确保复制用户有足够的权限
3. **配置错误**: 验证postgresql.conf中的设置
4. **版本不兼容**: 确保PostgreSQL版本支持逻辑复制

### 日志检查

```sql
-- 查看PostgreSQL日志
SELECT * FROM pg_stat_activity WHERE application_name LIKE '%replication%';

-- 查看订阅状态
SELECT * FROM pg_subscription WHERE srstate != 'r';
```

## 安全注意事项

1. 使用SSL连接保护数据传输
2. 定期更改数据库密码
3. 限制复制用户的权限
4. 监控复制连接的安全性

## 性能优化

1. 合理设置`synchronized_commit`参数
2. 监控WAL文件大小
3. 定期清理过期的复制槽
4. 根据网络带宽调整复制参数