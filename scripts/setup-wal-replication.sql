-- PostgreSQL WAL Replication Setup Script
-- This script sets up the database for real WAL streaming

-- 1. Enable WAL level (must be set to logical)
-- This requires superuser privileges:
-- ALTER SYSTEM SET wal_level = logical;
-- SELECT pg_reload_conf();

-- 2. Create publication for orders table
-- This publishes all changes to the orders table
CREATE PUBLICATION IF NOT EXISTS orders_publication
    FOR TABLE orders
    WITH (publish = 'insert, update, delete');

-- 3. Create publication for outbox_events table (for CDC integration)
CREATE PUBLICATION IF NOT EXISTS outbox_events_publication
    FOR TABLE "OutboxEvents"
    WITH (publish = 'insert, update, delete');

-- 4. Create replication slot for WAL streaming
-- This slot will receive all WAL changes for the publications above
SELECT pg_create_logical_replication_slot(
    'orders_replication_slot',    -- slot name
    'pgoutput',                  -- plugin
    true                           -- temporary slot (can be removed if needed)
);

-- 5. Grant replication privileges to the application user
-- Replace 'your_app_user' with your actual database user
-- GRANT rds_replication TO your_app_user;
-- GRANT SELECT ON pg_replication_slots TO your_app_user;

-- 6. Verify setup
SELECT
    s.slot_name,
    s.plugin,
    s.slot_type,
    s.database,
    s.active,
    p.pubname,
    p.pubowner,
    p.puballtables
FROM pg_replication_slots s
LEFT JOIN pg_publication p ON p.oid = s.database::oid
WHERE s.slot_name = 'orders_replication_slot'
OR p.pubname IN ('orders_publication', 'outbox_events_publication');

-- 7. Show current replication connections
SELECT
    pid,
    state,
    application_name,
    backend_start,
    query,
    wait_event_type,
    wait_event
FROM pg_stat_activity
WHERE backend_type = 'walsender'
   OR query LIKE '%replication%'
   OR application_name = 'wal_streaming_subscriber';

-- 8. Test replication slot status
SELECT
    slot_name,
    plugin,
    slot_type,
    database,
    active,
    active_pid,
    restart_lsn,
    confirmed_flush_lsn,
    wal_status,
    safe_wal_size
FROM pg_replication_slots
WHERE slot_name = 'orders_replication_slot';

-- 9. Check publication contents
SELECT
    p.pubname,
    n.nspname as schema_name,
    c.relname as table_name,
    p.pubinsert,
    p.pubupdate,
    pubdelete
FROM pg_publication_tables pt
JOIN pg_publication p ON p.oid = pt.pubid
JOIN pg_class c ON c.oid = pt.relid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE p.pubname IN ('orders_publication', 'outbox_events_publication')
ORDER BY p.pubname, n.nspname, c.relname;

-- 10. Test the setup (optional)
-- INSERT INTO orders (id, amount, created_at, customername, status)
-- VALUES (gen_random_uuid(), 100.00, NOW(), 'Test Customer', 'pending');
--
-- Wait for a few seconds and check if changes appear in the logs
--
-- DELETE FROM orders WHERE customername = 'Test Customer';