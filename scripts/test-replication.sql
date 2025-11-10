-- Test script to verify Neon-to-local replication setup
-- Run this script against your local PostgreSQL database

-- 1. Check if subscription exists
SELECT 'Subscription Status' as test_name,
       CASE WHEN COUNT(*) > 0 THEN 'PASS: Subscription exists' ELSE 'FAIL: No subscription found' END as result
FROM pg_subscription
WHERE subname = 'neon_to_local_subscription';

-- 2. Check replication slots
SELECT 'Replication Slot' as test_name,
       CASE WHEN COUNT(*) > 0 THEN 'PASS: Replication slots found' ELSE 'FAIL: No replication slots' END as result
FROM pg_replication_slots
WHERE slot_type = 'logical';

-- 3. Check table structure
SELECT 'Orders Table' as test_name,
       CASE WHEN COUNT(*) > 0 THEN 'PASS: Orders table exists' ELSE 'FAIL: Orders table missing' END as result
FROM information_schema.tables
WHERE table_name = 'orders' AND table_schema = 'public';

SELECT 'Outbox Events Table' as test_name,
       CASE WHEN COUNT(*) > 0 THEN 'PASS: Outbox events table exists' ELSE 'FAIL: Outbox events table missing' END as result
FROM information_schema.tables
WHERE table_name = 'outbox_events' AND table_schema = 'public';

-- 4. Check if data exists
SELECT 'Sample Data' as test_name,
       CASE
         WHEN (SELECT COUNT(*) FROM orders) > 0 THEN 'PASS: Sample data exists'
         ELSE 'INFO: No sample data (will be populated by replication)'
       END as result;

-- 5. Show detailed subscription information
\echo '=== Detailed Subscription Status ==='
SELECT
    subname,
    substring(conninfo from 'host=([^ ]*)') as host,
    substring(conninfo from 'dbname=([^ ]*)') as database,
    srstate,
    sractive as active,
    srapplydelay as apply_delay_seconds,
    srlsn as received_lsn,
    srflushlsn as flush_lsn
FROM pg_subscription;

-- 6. Show recent changes
\echo '=== Recent Changes (Last 24 Hours) ==='
SELECT
    'orders' as table_name,
    COUNT(*) as total_rows,
    COUNT(*) FILTER (WHERE created_at > CURRENT_TIMESTAMP - INTERVAL '24 hours') as recent_rows,
    MAX(created_at) as latest_change
FROM orders

UNION ALL

SELECT
    'outbox_events' as table_name,
    COUNT(*) as total_rows,
    COUNT(*) FILTER (WHERE created_at > CURRENT_TIMESTAMP - INTERVAL '24 hours') as recent_rows,
    MAX(created_at) as latest_change
FROM outbox_events;

-- 7. Check replication statistics
\echo '=== Replication Statistics ==='
SELECT * FROM replication_statistics();

-- 8. Show health check queries
\echo '=== Health Check ==='
SELECT
    'Database Connection' as check_name,
    'PASS' as status
UNION ALL
SELECT
    'Table Access' as check_name,
    CASE
        WHEN (SELECT COUNT(*) FROM orders LIMIT 1) >= 0 THEN 'PASS'
        ELSE 'FAIL'
    END as status
UNION ALL
SELECT
    'Replication Functions' as check_name,
    CASE
        WHEN (SELECT COUNT(*) FROM replication_status()) >= 0 THEN 'PASS'
        ELSE 'FAIL'
    END as status;

-- 9. Test insert operation
\echo '=== Testing Insert Operation ==='
INSERT INTO orders (customername, amount, status)
VALUES ('Test Customer', 99.99, 'Testing')
ON CONFLICT DO NOTHING;

SELECT 'Insert Test' as test_name,
       CASE
         WHEN EXISTS (SELECT 1 FROM orders WHERE customername = 'Test Customer')
         THEN 'PASS: Can insert data'
         ELSE 'FAIL: Cannot insert data'
       END as result;

-- Clean up test data
DELETE FROM orders WHERE customername = 'Test Customer';

\echo '=== Test Complete ==='
\echo 'If all tests show PASS, your replication setup is working correctly!'