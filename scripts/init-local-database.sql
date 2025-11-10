-- Initialize local database schema for Neon replication
-- This script creates the necessary tables and schema structure

-- Create orders table (matching Neon schema)
CREATE TABLE IF NOT EXISTS orders (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customername VARCHAR(255) NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'Pending',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    version INTEGER DEFAULT 1
);

-- Create outbox_events table for CDC tracking
CREATE TABLE IF NOT EXISTS outbox_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type VARCHAR(100) NOT NULL,
    event_data JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    processed_at TIMESTAMP WITH TIME ZONE,
    retry_count INTEGER DEFAULT 0,
    status VARCHAR(20) DEFAULT 'Pending'
);

-- Create replication tracking table
CREATE TABLE IF NOT EXISTS replication_log (
    id BIGSERIAL PRIMARY KEY,
    source_lsn pg_lsn,
    target_lsn pg_lsn,
    table_name VARCHAR(255),
    operation_type VARCHAR(20),
    replication_timestamp TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    status VARCHAR(20) DEFAULT 'Success',
    error_message TEXT
);

-- Create indexes for better performance
CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders(created_at);
CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status);
CREATE INDEX IF NOT EXISTS idx_orders_customername ON orders(customername);
CREATE INDEX IF NOT EXISTS idx_outbox_events_created_at ON outbox_events(created_at);
CREATE INDEX IF NOT EXISTS idx_outbox_events_status ON outbox_events(status);
CREATE INDEX IF NOT EXISTS idx_replication_log_timestamp ON replication_log(replication_timestamp);
CREATE INDEX IF NOT EXISTS idx_replication_log_status ON replication_log(status);

-- Create trigger for updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    NEW.version = OLD.version + 1;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Apply trigger to orders table
DROP TRIGGER IF EXISTS update_orders_updated_at ON orders;
CREATE TRIGGER update_orders_updated_at
    BEFORE UPDATE ON orders
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Create view for monitoring recent changes
CREATE OR REPLACE VIEW recent_changes AS
SELECT
    'orders' as table_name,
    'INSERT' as operation_type,
    id,
    customername,
    amount,
    status,
    created_at,
    updated_at
FROM orders
WHERE created_at > CURRENT_TIMESTAMP - INTERVAL '1 hour'

UNION ALL

SELECT
    'outbox_events' as table_name,
    'INSERT' as operation_type,
    id,
    event_type as customername,
    NULL as amount,
    status,
    created_at,
    processed_at as updated_at
FROM outbox_events
WHERE created_at > CURRENT_TIMESTAMP - INTERVAL '1 hour';

-- Grant permissions to replication user
GRANT SELECT, INSERT, UPDATE, DELETE ON orders TO replication_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON outbox_events TO replication_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON replication_log TO replication_user;
GRANT SELECT ON recent_changes TO replication_user;

-- Grant usage of sequences
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO replication_user;

-- Set default privileges for future objects
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO replication_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO replication_user;

-- Create function for replication statistics
CREATE OR REPLACE FUNCTION replication_statistics()
RETURNS TABLE(
    table_name text,
    total_rows bigint,
    recent_changes_24h bigint,
    last_change timestamp with time zone
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        'orders'::text,
        (SELECT COUNT(*) FROM orders),
        (SELECT COUNT(*) FROM orders WHERE created_at > CURRENT_TIMESTAMP - INTERVAL '24 hours'),
        (SELECT MAX(created_at) FROM orders)

    UNION ALL

    SELECT
        'outbox_events'::text,
        (SELECT COUNT(*) FROM outbox_events),
        (SELECT COUNT(*) FROM outbox_events WHERE created_at > CURRENT_TIMESTAMP - INTERVAL '24 hours'),
        (SELECT MAX(created_at) FROM outbox_events);
END;
$$ LANGUAGE plpgsql;

-- Grant usage of statistics function
GRANT EXECUTE ON FUNCTION replication_statistics() TO replication_user;
GRANT EXECUTE ON FUNCTION replication_statistics() TO postgres;

-- Initialize with sample data (optional - can be removed for production)
INSERT INTO orders (customername, amount, status)
SELECT
    'Sample Customer ' || generate_series,
    ROUND((random() * 1000)::numeric, 2),
    CASE WHEN random() > 0.5 THEN 'Completed' ELSE 'Pending' END
FROM generate_series(1, 5)
ON CONFLICT DO NOTHING;

-- Log initialization
DO $$
BEGIN
    RAISE NOTICE 'Local database initialized successfully';
    RAISE NOTICE 'Tables created: orders, outbox_events, replication_log';
    RAISE NOTICE 'Views created: recent_changes';
    RAISE NOTICE 'Functions created: replication_status(), replication_statistics()';
END $$;