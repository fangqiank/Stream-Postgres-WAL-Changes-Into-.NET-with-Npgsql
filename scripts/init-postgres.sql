-- Enable logical replication for Debezium
CREATE SYSTEM PARAMETER wal_level = logical;

-- Create replication user
CREATE USER debezium_user WITH REPLICATION PASSWORD 'debezium_password';

-- Grant necessary permissions
GRANT rds_replication TO debezium_user;

-- Create schema and tables
CREATE SCHEMA IF NOT EXISTS demo;

CREATE TABLE demo.products (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    price DECIMAL(10,2) NOT NULL,
    description VARCHAR(500),
    stock INTEGER DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE TABLE demo.categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE TABLE demo.orders (
    id SERIAL PRIMARY KEY,
    order_number VARCHAR(100) NOT NULL UNIQUE,
    customer_id INTEGER NOT NULL,
    total_amount DECIMAL(10,2) NOT NULL,
    status VARCHAR(50) DEFAULT 'pending',
    order_date TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Create indexes
CREATE INDEX idx_products_name ON demo.products(name);
CREATE INDEX idx_orders_status ON demo.orders(status);
CREATE INDEX idx_orders_date ON demo.orders(order_date);

-- Insert sample data
INSERT INTO demo.categories (name, description) VALUES
('Electronics', 'Electronic devices and accessories'),
('Books', 'Books and educational materials'),
('Clothing', 'Clothing and apparel');

INSERT INTO demo.products (name, price, description, stock) VALUES
('Laptop', 999.99, 'High-performance laptop', 50),
('Programming Book', 49.99, 'Learn to code effectively', 100),
('T-Shirt', 19.99, 'Comfortable cotton t-shirt', 200);

-- Grant permissions to debezium user
GRANT SELECT ON demo.products TO debezium_user;
GRANT SELECT ON demo.categories TO debezium_user;
GRANT SELECT ON demo.orders TO debezium_user;
GRANT SELECT ON ALL TABLES IN SCHEMA demo TO debezium_user;
GRANT USAGE ON SCHEMA demo TO debezium_user;