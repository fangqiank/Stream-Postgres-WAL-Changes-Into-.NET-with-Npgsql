# PostgreSQL 逻辑复制文档

## 📋 目录
1. [概述](#概述)
2. [架构设计](#架构设计)
3. [配置说明](#配置说明)
4. [API端点](#api端点)
5. [管理操作](#管理操作)
6. [监控和诊断](#监控和诊断)
7. [故障排除](#故障排除)

## 概述

本项目实现了基于PostgreSQL逻辑复制的数据同步系统，用于在源数据库(Neon)和目标数据库(Local)之间实时同步数据。

### 核心特性
- ✅ **实时数据同步**: 基于PostgreSQL原生逻辑复制
- ✅ **自动表管理**: 支持动态添加新表到复制
- ✅ **完整监控**: 提供复制状态和性能监控
- ✅ **RESTful API**: 完整的管理和诊断API
- ✅ **冲突避免**: 智能服务冲突检测和解决

## 架构设计

### 复制流程
```
Neon (源数据库)           Local (目标数据库)
┌─────────────────┐         ┌─────────────────┐
│  Orders 表      │         │  Orders 表      │
│  OutboxEvents 表│         │  OutboxEvents 表│
│  [新表...]        │         │  [新表...]        │
└─────────────────┘         └─────────────────┘
         │                           │
         │  logical replication   │
         └───────────────────────┘
               PostgreSQL pgoutput
```

### 核心组件
- **PostgreSqlLogicalReplicationService**: 主要复制服务
- **LogicalReplicationEndpoints**: API管理端点
- **ReplicationHealthMonitor**: 健康监控服务

## 配置说明

### appsettings.json 配置

```json
{
  "LogicalReplication": {
    "Enabled": true,
    "PublicationName": "neon_publication",
    "SubscriptionName": "local_subscription",
    "ReplicationSlotName": "neon_replication_slot",
    "TablesToReplicate": [ "Orders", "OutboxEvents" ],
    "StartupDelay": "00:00:05",
    "ConnectionTimeout": "00:00:30",
    "CommandTimeout": "00:05:00",
    "HeartbeatInterval": "00:00:10",
    "AutoCreatePublicationAndSubscription": true,
    "CopyExistingDataOnStart": true,
    "RetryInterval": "00:00:30",
    "MaxRetryAttempts": 10
  }
}
```

### 连接字符串配置
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=neon-host;Database=neondb;Username=postgres;Password=password",
  "LocalConnection": "Host=localhost;Port=5432;Database=localdb;Username=postgres;Password=localpostgres123;SSL Mode=Prefer;Trust Server Certificate=true"
}
```

## API端点

### 复制管理端点

#### 1. 获取复制状态
```http
GET /api/logical-replication/status
```

#### 2. 查看发布的表
```http
GET /api/logical-replication/publication/tables
```

#### 3. 添加表到发布
```http
POST /api/logical-replication/publication/add-tables
Content-Type: application/json

{
  "publicationName": "neon_publication",
  "tables": ["NewTable", "AnotherTable"]
}
```

#### 4. 获取复制延迟
```http
GET /api/logical-replication/lag
```

#### 5. 全面诊断
```http
GET /api/logical-replication/diagnose
```

### 公共诊断端点（无需认证）

#### 1. 公共复制诊断
```http
GET /api/public/replication-diagnose
```

#### 2. 测试复制
```http
GET /test-replication
```

## 管理操作

### 1. 创建发布和订阅

系统会自动创建发布和订阅：

```sql
-- 在源数据库创建发布
CREATE PUBLICATION neon_publication FOR TABLE "Orders", "OutboxEvents";

-- 在目标数据库创建订阅
CREATE SUBSCRIPTION local_subscription
CONNECTION 'host=neon-host port=5432 dbname=neondb user=postgres password=password'
PUBLICATION neon_publication
WITH (copy_data = true);
```

### 2. 添加新表到复制

#### 方法1: 通过API
```bash
curl -X POST http://localhost:5142/api/logical-replication/publication/add-tables \
  -H "Content-Type: application/json" \
  -d '{
    "publicationName": "neon_publication",
    "tables": ["NewTable", "Products", "Categories"]
  }'
```

#### 方法2: 直接SQL
```sql
-- 在源数据库执行
ALTER PUBLICATION neon_publication ADD TABLE "NewTable";
ALTER PUBLICATION neon_publication ADD TABLE "Products", "Categories";
```

### 3. 表结构同步要求

1. **主键要求**: 所有复制的表必须有主键
2. **表名一致**: 源数据库和目标数据库的表名必须完全匹配
3. **权限设置**: 复制用户需要有表的SELECT、INSERT、UPDATE、DELETE权限
4. **索引同步**: 索引需要在目标数据库手动创建

## 监控和诊断

### 1. 复制状态监控

#### 检查订阅状态
```sql
SELECT
    s.subname,
    s.subenabled,
    s.subslotname,
    CASE WHEN sr.pid IS NOT NULL THEN 'ACTIVE' ELSE 'INACTIVE' END as worker_status,
    sr.backend_start as replication_start_time
FROM pg_subscription s
LEFT JOIN pg_stat_replication sr ON sr.application_name = s.subname;
```

#### 检查复制延迟
```sql
SELECT
    s.subname,
    pg_wal_lsn_diff(pg_current_wal_lsn(), sr.replay_lsn) as lag_bytes,
    sr.flush_lsn,
    sr.replay_lsn,
    sr.sync_state
FROM pg_subscription s
LEFT JOIN pg_stat_replication sr ON sr.application_name = s.subname;
```

#### 检查复制槽状态
```sql
SELECT
    slot_name,
    slot_type,
    database,
    active,
    CASE WHEN restart_lsn IS NOT NULL THEN
        pg_wal_lsn_diff(pg_current_wal_lsn(), restart_lsn)
    ELSE 0 END as lag_bytes
FROM pg_replication_slots
WHERE slot_type = 'logical';
```

### 2. 数据同步验证

#### 比较记录数量
```sql
-- 源数据库
SELECT 'Source', COUNT(*) as count FROM "Orders"
UNION ALL
-- 目标数据库
SELECT 'Target', COUNT(*) as count FROM "Orders";
```

#### 检查最新活动
```sql
SELECT
    schemaname,
    tablename,
    n_tup_ins as inserts,
    n_tup_upd as updates,
    n_tup_del as deletes,
    last_vacuum,
    last_analyze
FROM pg_stat_user_tables
WHERE tablename IN ('Orders', 'OutboxEvents')
ORDER BY tablename;
```

### 3. 应用层监控

#### 使用诊断API
```bash
curl http://localhost:5142/api/public/replication-diagnose | jq .
```

#### 监控日志关键字
- `📋 发布已存在`
- `📋 订阅已存在`
- `✅ PostgreSQL逻辑复制服务已启动`
- `❌ 监控复制状态失败`

## 故障排除

### 常见问题和解决方案

#### 1. 复制延迟过高

**症状**: 数据同步慢或中断

**排查步骤**:
1. 检查网络连接
2. 检查源数据库负载
3. 检查WAL日志大小
4. 重启复制进程

**解决方案**:
```sql
-- 重启订阅
ALTER SUBSCRIPTION local_subscription DISABLE;
ALTER SUBSCRIPTION local_subscription ENABLE;
```

#### 2. 表结构不匹配

**症状**: 复制错误或数据不完整

**排查步骤**:
1. 比较源数据库和目标数据库的表结构
2. 检查列名、数据类型、约束
3. 验证主键设置

**解决方案**:
```sql
-- 在目标数据库同步表结构
CREATE TABLE IF NOT EXISTS "NewTable" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(100) NOT NULL,
    -- 其他列...
);
```

#### 3. 权限问题

**症状**: 复制进程无法启动

**排查步骤**:
1. 检查复制用户权限
2. 验证数据库连接
3. 检查pg_hba.conf配置

**解决方案**:
```sql
-- 授予复制权限
GRANT rds_replication TO replication_user;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO replication_user;
```

#### 4. 复制槽问题

**症状**: 复制槽不活跃或损坏

**排查步骤**:
1. 检查复制槽状态
2. 查看PostgreSQL日志
3. 验证WAL级别

**解决方案**:
```sql
-- 删除并重新创建复制槽
SELECT pg_drop_replication_slot('neon_replication_slot');
-- 重新创建订阅会自动创建新槽
```

### 性能优化建议

1. **网络优化**: 确保源数据库和目标数据库之间有良好的网络连接
2. **资源分配**: 为PostgreSQL分配足够的内存和CPU
3. **WAL配置**: 适当调整WAL相关参数
4. **批量操作**: 避免大批量数据操作影响复制性能

## 最佳实践

1. **表设计**: 确保所有表都有主键
2. **命名规范**: 使用一致的表名和列名命名规范
3. **权限管理**: 使用专用的复制用户账户
4. **监控告警**: 设置复制延迟和错误告警
5. **备份策略**: 定期备份复制配置和数据
6. **测试验证**: 在生产环境使用前充分测试

## 技术支持

如需技术支持，请提供：
1. 错误日志
2. 配置信息
3. 数据库版本信息
4. 网络环境详情

---

*本文档基于PostgreSQL逻辑复制实现，版本日期: 2025-11-10*