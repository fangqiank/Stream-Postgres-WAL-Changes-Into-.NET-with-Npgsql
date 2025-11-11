# Debezium Server 部署验证指南

## 🎯 概述

本指南提供了 Debezium Server Docker 配置的完整验证流程，确保每个组件都正确配置和运行。

## 📋 部署前检查清单

### 1. 系统要求验证

```bash
# 检查 Docker 版本
docker --version
# 应该显示 Docker version 20.10+ 或更高版本

# 检查 Docker Compose 版本
docker-compose --version
# 应该显示 docker-compose version 2.0+ 或更高版本

# 检查可用磁盘空间
df -h
# 至少需要 20GB 可用空间

# 检查内存
free -h
# 推荐至少 8GB RAM

# 检查端口占用
netstat -an | grep -E ":(5432|5433|5434|5672|15672|8080|27017|1433|1434|1435)"
# 确保这些端口未被占用
```

### 2. 文件准备验证

```bash
# 确保所有必需文件存在
ls -la
# 应该包含：
# - docker-compose.yml
# - application.properties
# - init-db.sql
# - init-mongo.js
# - init-sqlserver.sql

# 验证配置文件语法
docker-compose config
# 应该没有语法错误

# 检查 application.properties 语法
grep -n "=" application.properties
# 确保所有配置项都有正确的键值对
```

## 🚀 逐步部署验证

### 第1步：启动基础服务

```bash
# 仅启动数据库服务
docker-compose up -d postgres-primary postgres-backup postgres-reporting mongodb sqlserver sqlserver-analytics sqlserver-archive

# 等待服务启动完成
sleep 30

# 验证数据库服务状态
docker-compose ps
```

#### 验证 PostgreSQL 服务

```bash
# 检查主 PostgreSQL 容器
docker logs postgres-primary --tail 20

# 连接验证
docker exec postgres-primary pg_isready -U postgres

# 检查数据库和表
docker exec postgres-primary psql -U postgres -d demo -c "\dt"

# 验证逻辑复制配置
docker exec postgres-primary psql -U postgres -d demo -c "SELECT * FROM pg_publication;"
```

#### 验证 MongoDB 服务

```bash
# 检查 MongoDB 容器
docker logs mongodb --tail 20

# 连接验证
docker exec mongodb mongo --eval "db.adminCommand('ismaster')"

# 检查数据库
docker exec mongodb mongo --eval "show dbs"
```

#### 验证 SQL Server 服务

```bash
# 检查 SQL Server 容器
docker logs sqlserver --tail 20

# 连接验证
docker exec sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'StrongPassword123!' -Q "SELECT 1"

# 检查所有 SQL Server 实例
docker-compose ps | grep sqlserver
```

### 第2步：启动消息代理服务

```bash
# 启动 RabbitMQ
docker-compose up -d rabbitmq

# 等待 RabbitMQ 启动
sleep 20

# 验证 RabbitMQ 状态
docker logs rabbitmq --tail 20
```

#### 验证 RabbitMQ 配置

```bash
# 检查 RabbitMQ 状态
docker exec rabbitmq rabbitmqctl status

# 创建虚拟主机（如果不存在）
docker exec rabbitmq rabbitmqctl add_vhost debezium 2>/dev/null || echo "Virtual host already exists"

# 设置用户权限
docker exec rabbitmq rabbitmqctl set_permissions -p debezium admin ".*" ".*" ".*"

# 验证虚拟主机
docker exec rabbitmq rabbitmqctl list_vhosts

# 验证用户权限
docker exec rabbitmq rabbitmqctl list_permissions -p debezium

# 创建交换机
docker exec rabbitmq rabbitmqadmin declare exchange name=debezium.events type=topic durable=true --vhost=debezium -u admin -p admin 2>/dev/null || echo "Exchange already exists"

# 验证交换机
docker exec rabbitmq rabbitmqctl list_exchanges --vhost=debezium
```

#### 访问 RabbitMQ 管理界面

```bash
# 测试管理界面访问
curl -u admin:admin http://localhost:15672/api/overview

# 在浏览器中访问
# URL: http://localhost:15672
# 用户名: admin
# 密码: admin
```

### 第3步：启动 Debezium Server

```bash
# 启动 Debezium Server
docker-compose up -d debezium-server

# 等待服务启动
sleep 30

# 检查 Debezium Server 日志
docker logs debezium-server --tail 50
```

#### 验证 Debezium Server 配置

```bash
# 检查容器状态
docker-compose ps debezium-server

# 检查健康状态
curl http://localhost:8080/q/health
# 应该返回类似: {"status":"UP",...}

# 检查配置文件加载
docker logs debezium-server | grep -i "loading.*configuration"

# 验证连接器状态
curl http://localhost:8080/connectors
```

#### 验证 PostgreSQL CDC 连接

```bash
# 检查连接器日志
docker logs debezium-server | grep -i postgres

# 验证复制槽创建
docker exec postgres-primary psql -U postgres -d demo -c "SELECT * FROM pg_replication_slots;"

# 验证发布状态
docker exec postgres-primary psql -U postgres -d demo -c "SELECT * FROM pg_publication;"

# 测试数据变更捕获
docker exec postgres-primary psql -U postgres -d demo -c "
INSERT INTO categories (name, description) VALUES ('Test', 'Test Description');
SELECT * FROM categories WHERE name = 'Test';
"
```

#### 验证 RabbitMQ 连接

```bash
# 检查队列创建
docker exec rabbitmq rabbitmqctl list_queues --vhost=debezium

# 检查绑定
docker exec rabbitmq rabbitmqctl list_bindings --vhost=debezium

# 监控消息流量
docker exec rabbitmq rabbitmqctl list_channels --vhost=debezium
```

### 第4步：启动 .NET 应用程序

```bash
# 启动 .NET 应用程序
dotnet run &

# 等待应用启动
sleep 15

# 检查应用日志
tail -f logs/app.log 2>/dev/null || echo "Check console output for application logs"
```

#### 验证 .NET 应用程序连接

```bash
# 检查应用健康状态
curl http://localhost:5269/health 2>/dev/null || curl http://localhost:5269/

# 检查 RabbitMQ 连接状态
curl http://localhost:5269/api/universal-sync/status 2>/dev/null || echo "API endpoint may differ"

# 检查同步管道状态
curl http://localhost:5269/api/universal-sync/metrics 2>/dev/null || echo "API endpoint may differ"
```

## 🔍 端到端验证测试

### 测试1：完整数据流验证

```bash
# 1. 在主 PostgreSQL 中插入测试数据
docker exec postgres-primary psql -U postgres -d demo -c "
INSERT INTO categories (name, description) VALUES
('Electronics', 'Electronic devices and accessories'),
('Books', 'Print and digital books'),
('Clothing', 'Apparel and fashion items')
RETURNING id;
"

# 2. 验证数据插入成功
docker exec postgres-primary psql -U postgres -d demo -c "SELECT * FROM categories WHERE name IN ('Electronics', 'Books', 'Clothing');"

# 3. 等待 CDC 处理
sleep 10

# 4. 检查 RabbitMQ 消息
docker exec rabbitmq rabbitmqctl list_queues --vhost=debezium

# 5. 检查备份数据库同步
docker exec postgres-backup psql -U postgres -d demo_backup -c "SELECT * FROM categories;"

# 6. 检查报告数据库同步
docker exec postgres-reporting psql -U postgres -d reporting_db -c "\dt" 2>/dev/null || echo "Reporting DB may have different schema"
```

### 测试2：数据更新验证

```bash
# 1. 更新数据
docker exec postgres-primary psql -U postgres -d demo -c "
UPDATE categories SET description = 'Updated: ' || description WHERE name = 'Electronics';
"

# 2. 等待同步
sleep 5

# 3. 验证更新同步
docker exec postgres-backup psql -U postgres -d demo_backup -c "
SELECT * FROM categories WHERE name = 'Electronics';
"
```

### 测试3：数据删除验证

```bash
# 1. 删除数据
docker exec postgres-primary psql -U postgres -d demo -c "
DELETE FROM categories WHERE name = 'Books';
"

# 2. 等待同步
sleep 5

# 3. 验证删除同步
docker exec postgres-backup psql -U postgres -d demo_backup -c "
SELECT * FROM categories WHERE name = 'Books';
"
```

## 🛠️ 故障排除命令

### 连接问题诊断

```bash
# 检查容器网络
docker network ls
docker network inspect debezium_debezium

# 测试容器间连接
docker exec debezium-server ping postgres-primary
docker exec debezium-server ping rabbitmq
docker exec postgres-primary ping rabbitmq

# 检查端口映射
docker-compose port postgres-primary 5432
docker-compose port rabbitmq 5672
docker-compose port debezium-server 8080
```

### 日志分析

```bash
# 实时查看所有服务日志
docker-compose logs -f

# 查看特定服务日志
docker-compose logs -f debezium-server
docker-compose logs -f rabbitmq
docker-compose logs -f postgres-primary

# 查看最近的错误日志
docker-compose logs --tail=100 | grep -i error
docker-compose logs --tail=100 | grep -i failed
docker-compose logs --tail=100 | grep -i exception
```

### 配置验证

```bash
# 验证 Debezium Server 配置
docker exec debezium-server cat /debezium/conf/application.properties

# 验证 PostgreSQL 配置
docker exec postgres-primary cat /var/lib/postgresql/data/postgresql.conf | grep -E "(wal_level|max_replication)"

# 验证 RabbitMQ 配置
docker exec rabbitmq cat /etc/rabbitmq/rabbitmq.conf | grep -v "^#"
```

## 📊 性能基准测试

### 基础性能测试

```bash
# 1. 批量插入测试
docker exec postgres-primary psql -U postgres -d demo -c "
INSERT INTO categories (name, description)
SELECT
    'Category ' || generate_series,
    'Description for category ' || generate_series
FROM generate_series(1, 100);
"

# 2. 记录开始时间
START_TIME=$(date +%s)

# 3. 等待同步完成
echo "Waiting for sync completion..."
while true; do
    COUNT=$(docker exec postgres-backup psql -U postgres -d demo_backup -tAc "SELECT COUNT(*) FROM categories;" 2>/dev/null || echo "0")
    if [ "$COUNT" -ge "103" ]; then  # 100 + 3 from previous tests
        break
    fi
    sleep 2
done

# 4. 计算同步时间
END_TIME=$(date +%s)
SYNC_TIME=$((END_TIME - START_TIME))
echo "Sync completed in ${SYNC_TIME} seconds"

# 5. 验证数据一致性
PRIMARY_COUNT=$(docker exec postgres-primary psql -U postgres -d demo -tAc "SELECT COUNT(*) FROM categories;")
BACKUP_COUNT=$(docker exec postgres-backup psql -U postgres -d demo_backup -tAc "SELECT COUNT(*) FROM categories;")

echo "Primary DB count: $PRIMARY_COUNT"
echo "Backup DB count: $BACKUP_COUNT"

if [ "$PRIMARY_COUNT" -eq "$BACKUP_COUNT" ]; then
    echo "✅ Data synchronization successful!"
else
    echo "❌ Data synchronization failed!"
fi
```

### 负载测试

```bash
# 并发插入测试
for i in {1..10}; do
    (
        docker exec postgres-primary psql -U postgres -d demo -c "
        INSERT INTO categories (name, description)
        VALUES ('Concurrent Category $i', 'Description $i');
        " &
    ) &
done
wait

# 检查所有数据是否同步
sleep 10
docker exec postgres-backup psql -U postgres -d demo_backup -c "SELECT COUNT(*) FROM categories WHERE name LIKE 'Concurrent%';"
```

## 📈 监控和维护

### 定期健康检查

```bash
# 创建健康检查脚本
cat > health-check.sh << 'EOF'
#!/bin/bash

echo "=== Debezium System Health Check ==="
echo "Timestamp: $(date)"
echo

# 检查容器状态
echo "Container Status:"
docker-compose ps

echo
echo "Service Health Checks:"

# PostgreSQL 检查
if docker exec postgres-primary pg_isready -U postgres >/dev/null 2>&1; then
    echo "✅ PostgreSQL Primary: Healthy"
else
    echo "❌ PostgreSQL Primary: Unhealthy"
fi

# RabbitMQ 检查
if curl -s -u admin:admin http://localhost:15672/api/overview >/dev/null 2>&1; then
    echo "✅ RabbitMQ: Healthy"
else
    echo "❌ RabbitMQ: Unhealthy"
fi

# Debezium Server 检查
if curl -s http://localhost:8080/q/health >/dev/null 2>&1; then
    echo "✅ Debezium Server: Healthy"
else
    echo "❌ Debezium Server: Unhealthy"
fi

# .NET Application 检查
if curl -s http://localhost:5269 >/dev/null 2>&1; then
    echo "✅ .NET Application: Healthy"
else
    echo "❌ .NET Application: Unhealthy"
fi

echo
echo "=== End Health Check ==="
EOF

chmod +x health-check.sh
./health-check.sh
```

### 日志轮转配置

```bash
# 配置 Docker 日志轮转
# 在 docker-compose.yml 中添加日志配置
cat >> docker-compose.yml << 'EOF'

# 为服务添加日志配置
logging:
  driver: "json-file"
  options:
    max-size: "10m"
    max-file: "3"
EOF
```

## 📝 验证完成总结

完成所有验证步骤后，您的 Debezium Server 系统应该：

1. ✅ 所有 Docker 容器正常运行
2. ✅ PostgreSQL CDC 配置正确并捕获变更
3. ✅ Debezium Server 成功连接到 PostgreSQL 和 RabbitMQ
4. ✅ RabbitMQ 正确接收和路由 CDC 消息
5. ✅ .NET 应用程序成功消费消息并同步到目标数据库
6. ✅ 端到端数据流验证通过
7. ✅ 性能基准测试完成
8. ✅ 监控和维护脚本就绪

如果所有验证都通过，您的 Debezium Universal Data Sync 系统已准备好用于生产环境！