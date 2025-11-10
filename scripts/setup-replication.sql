-- PostgreSQL Logical Replication Setup Script
-- Execute this on your Neon (source) database

-- Create publication for orders table
CREATE PUBLICATION orders_publication FOR TABLE orders;

-- Create publication for outbox events table (optional, for CDC events)
CREATE PUBLICATION outbox_events_publication FOR TABLE "OutboxEvents";

-- Create replication slot for WAL streaming using pgoutput plugin
SELECT pg_create_logical_replication_slot('orders_replication_slot', 'pgoutput');

-- Verify the publication was created
SELECT * FROM pg_publication WHERE pubname IN ('orders_publication', 'outbox_events_publication');

-- Verify the replication slot was created
SELECT * FROM pg_replication_slots WHERE slot_name = 'orders_replication_slot';

-- Check which tables are in the publications
SELECT * FROM pg_publication_tables WHERE pubname IN ('orders_publication', 'outbox_events_publication');

-- Grant necessary permissions (if needed)
-- ALTER PUBLICATION orders_publication OWNER TO your_user;
-- ALTER PUBLICATION outbox_events_publication OWNER TO your_user;