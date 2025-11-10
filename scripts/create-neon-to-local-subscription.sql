-- 在本地PostgreSQL中创建从Neon数据库的订阅
-- 这个脚本需要在本地PostgreSQL中执行

-- 1. 创建从Neon数据库的订阅
-- 注意：连接信息需要在执行时替换为实际的Neon连接信息

-- 创建订阅的命令格式：
-- CREATE SUBSCRIPTION neon_to_local_subscription
-- CONNECTION 'host=your-neon-hostname user=your-username password=your-password dbname=neondb port=5432 sslmode=require'
-- PUBLICATION cdc_publication
-- WITH (slot_name = neon_to_local_slot, create_slot = false, copy_data = true, synchronized_commit = off);

-- 由于安全考虑，这里提供的是订阅模板，需要根据实际的Neon连接信息进行调整

-- 2. 检查订阅状态的查询
SELECT
    subname,
    pubname,
    slotname,
    relname,
    srstate,
    srlsn,
    srflushlsn,
    srapplydelay,
    sractive
FROM pg_subscription_rel;

-- 3. 检查订阅信息
SELECT * FROM pg_subscription;

-- 4. 监控复制进度的查询
SELECT
    pid,
    state,
    backend_start,
    count(*) as active_connections
FROM pg_stat_activity
WHERE application_name = 'pg_logical_replication_worker'
    OR query LIKE '%neon_to_local_subscription%'
GROUP BY pid, state, backend_start;