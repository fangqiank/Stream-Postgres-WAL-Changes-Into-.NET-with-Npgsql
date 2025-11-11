# Debezium Server 配置详细文档

## 📋 目录
1. [系统概述](#系统概述)
2. [Docker 完整配置](#docker-完整配置)
3. [Debezium Server 核心配置](#debezium-server-核心配置)
4. [分步配置指南](#分步配置指南)
5. [故障排除指南](#故障排除指南)
6. [验证和测试](#验证和测试)
7. [管理界面](#管理界面)

## 🎯 系统概述

本文档详细描述了 Debezium Universal Data Sync 系统的完整 Docker 配置过程，特别关注 Debezium Server 的配置细节。

### 架构组件
```
PostgreSQL (Primary) → Debezium Server → RabbitMQ → .NET 9 Application → 多个目标数据库
```

### 核心服务
- **PostgreSQL Primary**: 主数据库，启用 CDC
- **Debezium Server**: CDC 捕获服务
- **RabbitMQ**: 消息代理
- **多个目标数据库**: 备份、分析、报告数据库
- **.NET 9 应用**: 数据同步服务

## 🐳 Docker 完整配置

### docker-compose.yml 完整配置

```yaml
version: '3.8'

services:
  # 主 PostgreSQL 数据库
  postgres-primary:
    image: debezium/postgres:16
    container_name: postgres-primary
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: demo
    ports:
      - "5432:5432"
    volumes:
      - postgres_primary_data:/var/lib/postgresql/data
      - ./init-db.sql:/docker-entrypoint-initdb.d/init-db.sql
    command: >
      -c wal_level=logical
      -c max_replication_slots=4
      -c max_wal_senders=4
      -c max_connections=200
    networks:
      - debezium

  # 备份 PostgreSQL 数据库
  postgres-backup:
    image: postgres:16
    container_name: postgres-backup
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: demo_backup
    ports:
      - "5433:5432"
    volumes:
      - postgres_backup_data:/var/lib/postgresql/data
    networks:
      - debezium

  # 报告 PostgreSQL 数据库
  postgres-reporting:
    image: postgres:16
    container_name: postgres-reporting
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: reporting_db
    ports:
      - "5434:5432"
    volumes:
      - postgres_reporting_data:/var/lib/postgresql/data
    networks:
      - debezium

  # MongoDB 分析数据库
  mongodb:
    image: mongo:7.0
    container_name: mongodb
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: admin
      MONGO_INITDB_DATABASE: debezium_analytics
    volumes:
      - mongodb_data:/data/db
      - ./init-mongo.js:/docker-entrypoint-initdb.d/init-mongo.js:ro
    networks:
      - debezium

  # SQL Server 数据仓库
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: sqlserver
    user: root
    environment:
      ACCEPT_EULA: "Y"
      SA_PASSWORD: "StrongPassword123!"
      MSSQL_PID: "Developer"
      MSSQL_AGENT_ENABLED: "true"
    ports:
      - "1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql/data
      - ./init-sqlserver.sql:/docker-entrypoint-initdb.d/init-sqlserver.sql:ro
    networks:
      - debezium
    privileged: true

  # SQL Server 分析数据库
  sqlserver-analytics:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: sqlserver-analytics
    user: root
    environment:
      ACCEPT_EULA: "Y"
      SA_PASSWORD: "StrongPassword123!"
      MSSQL_PID: "Developer"
      MSSQL_AGENT_ENABLED: "true"
    ports:
      - "1434:1433"
    volumes:
      - sqlserver_analytics_data:/var/opt/mssql/data
    networks:
      - debezium
    privileged: true

  # SQL Server 归档数据库
  sqlserver-archive:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: sqlserver-archive
    user: root
    environment:
      ACCEPT_EULA: "Y"
      SA_PASSWORD: "StrongPassword123!"
      MSSQL_PID: "Developer"
      MSSQL_AGENT_ENABLED: "true"
    ports:
      - "1435:1433"
    volumes:
      - sqlserver_archive_data:/var/opt/mssql/data
    networks:
      - debezium
    privileged: true

  # RabbitMQ 消息代理
  rabbitmq:
    image: rabbitmq:3.12-management
    container_name: rabbitmq
    ports:
      - "5672:5672"   # AMQP 端口
      - "15672:15672" # 管理 UI
    environment:
      RABBITMQ_DEFAULT_USER: admin
      RABBITMQ_DEFAULT_PASS: admin
      RABBITMQ_DEFAULT_VHOST: debezium
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    networks:
      - debezium

  # Debezium Server - 核心组件
  debezium-server:
    image: debezium/server:2.6
    container_name: debezium-server
    ports:
      - "8080:8080" # Debezium Server API
    environment:
      DEBEZIUM_SINK_RABBITMQ_VIRTUAL_HOST: debezium
    volumes:
      - debezium_data:/data
      - ./application.properties:/debezium/conf/application.properties
    depends_on:
      - rabbitmq
      - postgres-primary
    networks:
      - debezium

volumes:
  postgres_primary_data:
  postgres_backup_data:
  postgres_reporting_data:
  mongodb_data:
  sqlserver_data:
  sqlserver_analytics_data:
  sqlserver_archive_data:
  rabbitmq_data:
  debezium_data:

networks:
  debezium:
    driver: bridge
```

## ⚙️ Debezium Server 核心配置

### application.properties 配置文件

这是 Debezium Server 最关键的配置文件：

```properties
# ========================================
# Debezium Server 核心配置
# ========================================

# HTTP 服务器配置
quarkus.http.port=8080

# 日志配置
quarkus.log.level=INFO
quarkus.log.console.json=false

# ========================================
# 源数据库配置 (PostgreSQL)
# ========================================

# 连接器类型
debezium.source.connector.class=io.debezium.connector.postgresql.PostgresConnector

# 数据库连接信息
debezium.source.database.hostname=postgres-primary
debezium.source.database.port=5432
debezium.source.database.user=postgres
debezium.source.database.password=postgres
debezium.source.database.dbname=demo

# 逻辑复制配置
debezium.source.database.server.name=postgres-primary-server
debezium.source.plugin.name=pgoutput
debezium.source.slot.name=debezium_slot
debezium.source.publication.name=debezium_pub

# 主题前缀
debezium.source.topic.prefix=debezium

# 表和模式过滤
debezium.source.schema.include.list=public
debezium.source.table.include.list=public.*

# ========================================
# 消息接收器配置 (RabbitMQ)
# ========================================

# 接收器类型
debezium.sink.type=rabbitmq

# RabbitMQ 连接配置
debezium.sink.rabbitmq.connection.host=rabbitmq
debezium.sink.rabbitmq.connection.port=5672
debezium.sink.rabbitmq.connection.username=admin
debezium.sink.rabbitmq.connection.password=admin
debezium.sink.rabbitmq.connection.virtual-host=debezium

# 交换机配置
debezium.sink.rabbitmq.exchange=debezium.events
debezium.sink.rabbitmq.exchange.type=topic

# 消息路由配置
debezium.sink.rabbitmq.routing.key.format=${database}.${schema}.${table}
debezium.sink.rabbitmq.key.serializer=org.apache.kafka.connect.storage.StringConverter
debezium.sink.rabbitmq.value.serializer=io.debezium.converters.CloudEventsConverter

# ========================================
# 性能和可靠性配置
# ========================================

# 批处理配置
debezium.source.max.batch.size=1000
debezium.source.max.queue.size=8192

# 心跳配置
debezium.source.heartbeat.interval.ms=30000

# 事务配置
debezium.source.transaction.timeout.ms=600000

# 偏移量存储
debezium.source.offset.storage.file.filename=data/offsets.dat
```

### 配置文件关键参数说明

#### 源数据库配置参数

| 参数 | 说明 | 示例值 |
|------|------|--------|
| `debezium.source.connector.class` | 连接器类名 | `io.debezium.connector.postgresql.PostgresConnector` |
| `debezium.source.database.hostname` | 数据库主机名 | `postgres-primary` |
| `debezium.source.database.port` | 数据库端口 | `5432` |
| `debezium.source.database.user` | 数据库用户名 | `postgres` |
| `debezium.source.database.password` | 数据库密码 | `postgres` |
| `debezium.source.database.dbname` | 数据库名称 | `demo` |
| `debezium.source.plugin.name` | 逻辑复制插件 | `pgoutput` |
| `debezium.source.slot.name` | 复制槽名称 | `debezium_slot` |
| `debezium.source.publication.name` | 发布名称 | `debezium_pub` |
| `debezium.source.topic.prefix` | 主题前缀 | `debezium` |

#### RabbitMQ 接收器配置参数

| 参数 | 说明 | 示例值 |
|------|------|--------|
| `debezium.sink.type` | 接收器类型 | `rabbitmq` |
| `debezium.sink.rabbitmq.connection.host` | RabbitMQ 主机 | `rabbitmq` |
| `debezium.sink.rabbitmq.connection.port` | RabbitMQ 端口 | `5672` |
| `debezium.sink.rabbitmq.connection.username` | RabbitMQ 用户名 | `admin` |
| `debezium.sink.rabbitmq.connection.password` | RabbitMQ 密码 | `admin` |
| `debezium.sink.rabbitmq.connection.virtual-host` | 虚拟主机 | `debezium` |
| `debezium.sink.rabbitmq.exchange` | 交换机名称 | `debezium.events` |
| `debezium.sink.rabbitmq.exchange.type` | 交换机类型 | `topic` |

## 📋 分步配置指南

### 第1步：准备配置文件

1. **创建 docker-compose.yml 文件**
   ```bash
   # 使用上面提供的完整 docker-compose.yml 内容
   ```

2. **创建 application.properties 文件**
   ```bash
   # 使用上面提供的 Debezium Server 配置内容
   ```

3. **创建数据库初始化脚本**
   ```sql
   -- init-db.sql
   CREATE TABLE categories (
       id SERIAL PRIMARY KEY,
       name VARCHAR(100) NOT NULL,
       description TEXT,
       created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
       updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
   );

   CREATE TABLE products (
       id SERIAL PRIMARY KEY,
       category_id INTEGER REFERENCES categories(id),
       name VARCHAR(200) NOT NULL,
       price DECIMAL(10,2),
       created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
       updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
   );

   CREATE TABLE orders (
       id SERIAL PRIMARY KEY,
       product_id INTEGER REFERENCES products(id),
       quantity INTEGER NOT NULL,
       total_amount DECIMAL(10,2),
       order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
       status VARCHAR(50) DEFAULT 'pending'
   );
   ```

### 第2步：启动 Docker 服务

```bash
# 启动所有服务
docker-compose up -d

# 查看服务状态
docker-compose ps

# 查看服务日志
docker-compose logs -f
```

### 第3步：配置 RabbitMQ

```bash
# 等待 RabbitMQ 启动完成
docker exec rabbitmq rabbitmqctl wait --timeout 60 /var/lib/rabbitmq/mnesia/rabbit@rabbitmq.pid

# 创建必要的虚拟主机（如果不存在）
docker exec rabbitmq rabbitmqctl add_vhost debezium || echo "Virtual host already exists"

# 设置用户权限
docker exec rabbitmq rabbitmqctl set_permissions -p debezium admin ".*" ".*" ".*"

# 创建交换机
docker exec rabbitmq rabbitmqadmin declare exchange name=debezium.events type=topic durable=true --vhost=debezium -u admin -p admin
```

### 第4步：验证 Debezium Server

```bash
# 检查 Debezium Server 日志
docker logs debezium-server

# 检查健康状态
curl http://localhost:8080/q/health

# 检查连接器状态
curl http://localhost:8080/connectors
```

## 🔧 故障排除指南

### 常见问题及解决方案

#### 1. Debezium Server 配置文件未加载

**问题**: 错误信息 `SRCFG00014: The config property debezium.sink.type is required`

**原因**: 配置文件路径不正确或文件不存在

**解决方案**:
```bash
# 检查文件是否存在
ls -la application.properties

# 确保 docker-compose.yml 中的路径正确
volumes:
  - ./application.properties:/debezium/conf/application.properties

# 重新启动 Debezium Server
docker-compose restart debezium-server
```

#### 2. RabbitMQ 连接失败

**问题**: `CONNECTION_REFUSED: localhost:5672`

**原因**: Debezium Server 尝试连接到 localhost 而不是容器名

**解决方案**:
```properties
# 修改 application.properties 中的连接配置
debezium.sink.rabbitmq.connection.host=rabbitmq  # 不是 localhost
```

#### 3. 虚拟主机不存在

**问题**: `NOT_ALLOWED - vhost / not found`

**原因**: RabbitMQ 虚拟主机未创建

**解决方案**:
```bash
# 创建根虚拟主机
docker exec rabbitmq rabbitmqadd_vhost '/'
docker exec rabbitmq rabbitmqctl set_permissions -p '/' admin '.*' '.*' '.*'

# 或者使用 debezium 虚拟主机
docker exec rabbitmq rabbitmqctl add_vhost debezium
docker exec rabbitmq rabbitmqctl set_permissions -p debezium admin '.*' '.*' '.*'
```

#### 4. 主题前缀缺失

**问题**: `The 'topic.prefix' value is invalid: A value is required`

**原因**: 缺少主题前缀配置

**解决方案**:
```properties
# 在 application.properties 中添加
debezium.source.topic.prefix=debezium
```

#### 5. RabbitMQ 交换机不存在

**问题**: `NOT_FOUND - no exchange 'debezium.events'`

**原因**: 交换机未创建

**解决方案**:
```bash
# 创建主题交换机
docker exec rabbitmq rabbitmqadmin declare exchange name=debezium.events type=topic durable=true --vhost=/ -u admin -p admin
```

#### 6. PostgreSQL 逻辑复制问题

**问题**: 复制槽或发布创建失败

**解决方案**:
```sql
-- 连接到 PostgreSQL
docker exec -it postgres-primary psql -U postgres -d demo

-- 手动创建发布
CREATE PUBLICATION debezium_pub FOR ALL TABLES;

-- 检查复制槽
SELECT * FROM pg_replication_slots;
```

### 调试命令

```bash
# 查看所有容器状态
docker ps -a

# 查看特定容器日志
docker logs debezium-server --tail 100
docker logs rabbitmq --tail 100
docker logs postgres-primary --tail 100

# 进入容器调试
docker exec -it debezium-server /bin/bash
docker exec -it rabbitmq /bin/bash

# 检查网络连接
docker exec debezium-server ping postgres-primary
docker exec debezium-server ping rabbitmq
```

## ✅ 验证和测试

### 1. 验证服务状态

```bash
# 检查所有服务
docker-compose ps

# 检查网络连接
docker network ls
docker network inspect debezium_debezium
```

### 2. 验证 PostgreSQL CDC

```bash
# 连接到 PostgreSQL
docker exec -it postgres-primary psql -U postgres -d demo

# 检查发布
SELECT * FROM pg_publication;

# 检查复制槽
SELECT * FROM pg_replication_slots;

# 测试数据变更
INSERT INTO categories (name, description) VALUES ('Test Category', 'Test Description');
UPDATE categories SET description = 'Updated Description' WHERE name = 'Test Category';
DELETE FROM categories WHERE name = 'Test Category';
```

### 3. 验证 RabbitMQ

```bash
# 检查 RabbitMQ 状态
docker exec rabbitmq rabbitmqctl status

# 检查队列
docker exec rabbitmq rabbitmqctl list_queues --vhost=debezium

# 检查交换机
docker exec rabbitmq rabbitmqctl list_exchanges --vhost=debezium
```

### 4. 验证 Debezium Server

```bash
# 健康检查
curl http://localhost:8080/q/health

# 检查连接器状态
curl http://localhost:8080/connectors

# 查看配置
curl http://localhost:8080/connectors/postgres-connector/config
```

## 🎛️ 管理界面

### 1. RabbitMQ 管理界面

- **URL**: http://localhost:15672
- **用户名**: admin
- **密码**: admin

**功能**:
- 监控队列状态
- 查看消息流量
- 管理交换机和绑定
- 查看连接和通道

### 2. Debezium Server API

- **基础 URL**: http://localhost:8080
- **健康端点**: http://localhost:8080/q/health
- **连接器 API**: http://localhost:8080/connectors

**常用 API**:
```bash
# 获取所有连接器
GET /connectors

# 获取特定连接器配置
GET /connectors/{connector-name}/config

# 暂停连接器
PUT /connectors/{connector-name}/pause

# 恢复连接器
PUT /connectors/{connector-name}/resume

# 删除连接器
DELETE /connectors/{connector-name}
```

## 📊 性能优化建议

### 1. Debezium Server 优化

```properties
# 增加批处理大小
debezium.source.max.batch.size=2000
debezium.source.max.queue.size=16384

# 调整心跳间隔
debezium.source.heartbeat.interval.ms=10000

# 优化内存使用
quarkus.datasource.jdbc.max-size=20
quarkus.datasource.jdbc.min-size=5
```

### 2. RabbitMQ 优化

```yaml
# 在 docker-compose.yml 中添加性能调优
rabbitmq:
  environment:
    RABBITMQ_DEFAULT_VHOST: debezium
    # 性能优化参数
    RABBITMQ_VM_MEMORY_HIGH_WATERMARK: 0.6
    RABBITMQ_DISK_FREE_LIMIT.absolute: 1GB
```

### 3. PostgreSQL 优化

```yaml
# 在 postgres-primary 的 command 中添加
command: >
  -c wal_level=logical
  -c max_replication_slots=10
  -c max_wal_senders=10
  -c max_connections=200
  -c shared_preload_libraries=pgoutput
  -c wal_keep_size=1GB
```

## 🔒 安全配置建议

### 1. 生产环境密码管理

```bash
# 使用 Docker secrets 或环境变量
echo "your_secure_password" | docker secret create postgres_password -

# 在 docker-compose.yml 中引用
secrets:
  postgres_password:
    external: true
```

### 2. SSL/TLS 配置

```properties
# Debezium Server SSL 配置
debezium.source.database.sslmode=verify-full
debezium.source.database.sslrootcert=/debezium/conf/ca.crt
debezium.source.database.sslcert=/debezium/conf/client.crt
debezium.source.database.sslkey=/debezium/conf/client.key
```

### 3. 网络安全

```yaml
# 使用自定义网络
networks:
  debezium-internal:
    driver: bridge
    internal: true  # 内部网络，不对外暴露
  debezium-external:
    driver: bridge
```

## 📝 总结

本文档提供了 Debezium Server 与 Docker 环境的完整配置指南。关键要点：

1. **正确的配置文件**: application.properties 是 Debezium Server 的核心配置
2. **网络配置**: 确保所有服务在同一个 Docker 网络中
3. **权限设置**: 正确配置 PostgreSQL 复制权限和 RabbitMQ 用户权限
4. **故障排除**: 使用日志和 API 端点进行问题诊断
5. **性能优化**: 根据实际需求调整批处理和连接参数

遵循本指南，您可以成功构建一个可靠、高性能的 CDC 数据同步系统。