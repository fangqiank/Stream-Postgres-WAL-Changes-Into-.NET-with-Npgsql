-- Create main database schema
CREATE TABLE products (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    price DECIMAL(10,2) NOT NULL,
    description TEXT,
    stock INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE orders (
    id SERIAL PRIMARY KEY,
    order_number VARCHAR(50) UNIQUE NOT NULL,
    customer_id INTEGER,
    total_amount DECIMAL(10,2) NOT NULL,
    status VARCHAR(20) DEFAULT 'pending' CHECK (status IN ('pending', 'processing', 'shipped', 'delivered', 'cancelled')),
    order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create indexes
CREATE INDEX idx_products_name ON products(name);
CREATE INDEX idx_orders_customer_id ON orders(customer_id);
CREATE INDEX idx_orders_status ON orders(status);
CREATE INDEX idx_orders_order_date ON orders(order_date);
CREATE INDEX idx_categories_name ON categories(name);

-- Create replication slot for Debezium
SELECT pg_create_logical_replication_slot('debezium_slot', 'pgoutput');

-- Grant replication permissions
ALTER USER postgres REPLICATION;
ALTER USER postgres SUPERUSER;

-- Create some initial data for testing
INSERT INTO products (name, price, description, stock) VALUES
('Laptop', 999.99, 'High-performance laptop with 16GB RAM', 50),
('Smartphone', 699.99, 'Latest smartphone with 5G connectivity', 100),
('Headphones', 199.99, 'Wireless noise-cancelling headphones', 75);

INSERT INTO orders (order_number, customer_id, total_amount, status) VALUES
('ORD-001', 1, 1199.98, 'processing'),
('ORD-002', 2, 699.99, 'pending'),
('ORD-003', 1, 199.99, 'shipped');

INSERT INTO categories (name, description) VALUES
('Electronics', 'Electronic devices and gadgets'),
('Accessories', 'Computer and mobile accessories'),
('Office', 'Office supplies and equipment');

-- Create function to auto-update updated_at
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Create triggers for updated_at
CREATE TRIGGER update_products_updated_at
    BEFORE UPDATE ON products
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_orders_updated_at
    BEFORE UPDATE ON orders
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_categories_updated_at
    BEFORE UPDATE ON categories
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();