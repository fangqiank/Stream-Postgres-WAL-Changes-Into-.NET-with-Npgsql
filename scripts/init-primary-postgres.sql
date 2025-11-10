-- Initialize primary PostgreSQL database
-- This script is executed when the primary PostgreSQL container starts

-- Create tables for the application
-- These tables will be used by the main application and synchronized to backup

CREATE TABLE IF NOT EXISTS products (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    price DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    description TEXT,
    stock INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS orders (
    id SERIAL PRIMARY KEY,
    order_number VARCHAR(100) NOT NULL UNIQUE,
    customer_id INTEGER,
    total_amount DECIMAL(10,2) NOT NULL DEFAULT 0.00,
    status VARCHAR(50) NOT NULL DEFAULT 'pending',
    order_date TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Create indexes for better performance
CREATE INDEX IF NOT EXISTS idx_products_name ON products(name);
CREATE INDEX IF NOT EXISTS idx_products_created_at ON products(created_at);
CREATE INDEX IF NOT EXISTS idx_products_updated_at ON products(updated_at);
CREATE INDEX IF NOT EXISTS idx_categories_name ON categories(name);
CREATE INDEX IF NOT EXISTS idx_categories_created_at ON categories(created_at);
CREATE INDEX IF NOT EXISTS idx_categories_updated_at ON categories(updated_at);
CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status);
CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(order_date);
CREATE INDEX IF NOT EXISTS idx_orders_updated_at ON orders(updated_at);
CREATE INDEX IF NOT EXISTS idx_orders_order_number ON orders(order_number);

-- Create function to automatically update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Create triggers to automatically update updated_at
DROP TRIGGER IF EXISTS update_products_updated_at ON products;
CREATE TRIGGER update_products_updated_at BEFORE UPDATE ON products
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DROP TRIGGER IF EXISTS update_categories_updated_at ON categories;
CREATE TRIGGER update_categories_updated_at BEFORE UPDATE ON categories
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DROP TRIGGER IF EXISTS update_orders_updated_at ON orders;
CREATE TRIGGER update_orders_updated_at BEFORE UPDATE ON orders
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Insert sample data for testing
INSERT INTO categories (name, description) VALUES
('Electronics', 'Electronic devices and accessories'),
('Books', 'Books and educational materials'),
('Clothing', 'Clothing and apparel')
ON CONFLICT DO NOTHING;

INSERT INTO products (name, price, description, stock) VALUES
('Laptop', 999.99, 'High-performance laptop with 16GB RAM and 512GB SSD', 50),
('Programming Book', 49.99, 'Learn to code effectively with practical examples', 100),
('T-Shirt', 19.99, 'Comfortable cotton t-shirt in various sizes', 200),
('Smartphone', 699.99, 'Latest smartphone with excellent camera quality', 75),
('Headphones', 149.99, 'Noise-cancelling wireless headphones', 30)
ON CONFLICT DO NOTHING;

INSERT INTO orders (order_number, customer_id, total_amount, status) VALUES
('ORD-001', 1, 1069.98, 'completed'),
('ORD-002', 2, 69.98, 'pending'),
('ORD-003', 3, 199.99, 'shipped'),
('ORD-004', 1, 849.98, 'processing')
ON CONFLICT (order_number) DO NOTHING;

-- Grant permissions
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO postgres;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO postgres;

-- Create replication slot for Debezium (for logical replication)
-- Note: This might fail if the slot already exists, which is fine during initialization
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = 'debezium_slot') THEN
        PERFORM pg_create_logical_replication_slot('debezium_slot', 'pgoutput');
    END IF;
END
$$;

-- Grant replication permissions to postgres user
ALTER USER postgres REPLICATION;
ALTER USER postgres SUPERUSER;

-- Log initialization
\echo 'Primary database initialized successfully'