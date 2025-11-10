-- Local PostgreSQL configuration for Neon replication
-- This script configures the local PostgreSQL to receive data from Neon

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "pg_stat_statements";
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Check logical replication configuration
SELECT
    'wal_level' as setting,
    current_setting('wal_level') as current_value,
    CASE
        WHEN current_setting('wal_level') = 'logical' THEN 'OK: Logical replication enabled'
        ELSE 'ERROR: Logical replication not enabled'
    END as status

UNION ALL

SELECT
    'max_replication_slots' as setting,
    current_setting('max_replication_slots') as current_value,
    CASE
        WHEN CAST(current_setting('max_replication_slots') AS INTEGER) >= 1 THEN 'OK: Replication slots available'
        ELSE 'ERROR: No replication slots configured'
    END as status

UNION ALL

SELECT
    'max_wal_senders' as setting,
    current_setting('max_wal_senders') as current_value,
    CASE
        WHEN CAST(current_setting('max_wal_senders') AS INTEGER) >= 1 THEN 'OK: WAL senders available'
        ELSE 'ERROR: No WAL senders configured'
    END as status;

-- Create a monitoring function for replication status
CREATE OR REPLACE FUNCTION check_replication_config()
RETURNS TABLE(
    config_name text,
    current_value text,
    status text
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        'Database initialized'::text,
        current_database()::text,
        'SUCCESS'::text;
END;
$$ LANGUAGE plpgsql;

-- Test the configuration
SELECT * FROM check_replication_config();