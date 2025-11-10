-- Create replication user for Neon-to-local replication
-- This script creates a dedicated user for logical replication

-- Create replication role
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'replication_user') THEN
        CREATE ROLE replication_user WITH LOGIN PASSWORD 'replication_password_123' REPLICATION CONNECTION LIMIT 10;
    END IF;
END
$$;

-- Grant necessary permissions
GRANT CONNECT ON DATABASE localdb TO replication_user;
GRANT USAGE ON SCHEMA public TO replication_user;
GRANT CREATE ON SCHEMA public TO replication_user;
GRANT ALL ON SCHEMA public TO replication_user;

-- Set default privileges for future tables
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO replication_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO replication_user;

-- Create pgcrypto extension for UUID generation
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Create replication monitoring function
CREATE OR REPLACE FUNCTION replication_status()
RETURNS TABLE(
    slot_name text,
    plugin text,
    database text,
    active boolean,
    restart_lsn pg_lsn
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        s.slot_name,
        s.plugin,
        s.database,
        s.active,
        s.restart_lsn
    FROM pg_replication_slots s
    WHERE s.slot_type = 'logical';
END;
$$ LANGUAGE plpgsql;

-- Grant usage of monitoring function
GRANT EXECUTE ON FUNCTION replication_status() TO replication_user;
GRANT EXECUTE ON FUNCTION replication_status() TO postgres;

-- Log replication user creation
DO $$
BEGIN
    RAISE NOTICE 'Replication user created successfully: %', current_user;
    RAISE NOTICE 'Database: %, User: replication_user', current_database();
END $$;