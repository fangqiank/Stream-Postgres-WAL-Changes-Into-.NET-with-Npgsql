using Microsoft.AspNetCore.Mvc;
using DebeziumDemoApp.Core.Interfaces;
using DebeziumDemoApp.Core.Services;

namespace DebeziumDemoApp.Extensions;

/// <summary>
/// UI API endpoints for the frontend interface
/// </summary>
public static class UiApiEndpoints
{
    public static IEndpointRouteBuilder MapUiApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Products endpoints
        endpoints.MapGet("/api/products", async () =>
        {
            // Return mock data for now - in real implementation, this would query the database
            var products = new[]
            {
                new { id = 1, name = "Laptop", price = 999.99m, stock = 50, category = "Electronics", created_at = DateTime.UtcNow, updated_at = DateTime.UtcNow },
                new { id = 2, name = "Mouse", price = 29.99m, stock = 150, category = "Electronics", created_at = DateTime.UtcNow, updated_at = DateTime.UtcNow },
                new { id = 3, name = "Keyboard", price = 79.99m, stock = 75, category = "Electronics", created_at = DateTime.UtcNow, updated_at = DateTime.UtcNow }
            };

            return Results.Ok(products);
        })
        .WithName("GetProducts")
        .WithTags("UI API");

        // Orders endpoints
        endpoints.MapGet("/api/orders", async () =>
        {
            // Return mock data for now - ensure all numeric fields have proper decimal values
            var orders = new[]
            {
                new { id = 1, orderNumber = "ORD-2024-001", customerId = "CUST-001", totalAmount = 1109.97m, status = "completed", orderDate = DateTime.UtcNow, items = new[] { "Laptop", "Mouse" }, shippingCost = 10.00m, tax = 89.97m },
                new { id = 2, orderNumber = "ORD-2024-002", customerId = "CUST-002", totalAmount = 79.99m, status = "pending", orderDate = DateTime.UtcNow, items = new[] { "Keyboard" }, shippingCost = 5.00m, tax = 6.40m },
                new { id = 3, orderNumber = "ORD-2024-003", customerId = "CUST-003", totalAmount = 209.98m, status = "processing", orderDate = DateTime.UtcNow, items = new[] { "Mouse", "Keyboard" }, shippingCost = 7.50m, tax = 16.80m }
            };

            return Results.Ok(orders);
        })
        .WithName("GetOrders")
        .WithTags("UI API");

        // Categories endpoints
        endpoints.MapGet("/api/categories", async () =>
        {
            // Return mock data for now
            var categories = new[]
            {
                new { id = 1, name = "Electronics", description = "Electronic devices and accessories", product_count = 15, created_at = DateTime.UtcNow },
                new { id = 2, name = "Books", description = "Books and publications", product_count = 45, created_at = DateTime.UtcNow },
                new { id = 3, name = "Clothing", description = "Apparel and fashion items", product_count = 32, created_at = DateTime.UtcNow }
            };

            return Results.Ok(categories);
        })
        .WithName("GetCategories")
        .WithTags("UI API");

        // Backup Products endpoints
        endpoints.MapGet("/api/backup/products", async () =>
        {
            // Return mock backup data
            var products = new[]
            {
                new { id = 1, name = "Laptop", price = 999.99m, stock = 50, category = "Electronics", last_synced = DateTime.UtcNow, sync_status = "success" },
                new { id = 2, name = "Mouse", price = 29.99m, stock = 150, category = "Electronics", last_synced = DateTime.UtcNow, sync_status = "success" }
            };

            return Results.Ok(products);
        })
        .WithName("GetBackupProducts")
        .WithTags("UI API");

        // Backup Orders endpoints
        endpoints.MapGet("/api/backup/orders", async () =>
        {
            // Return mock backup data with proper decimal values
            var orders = new[]
            {
                new { id = 1, customer_name = "John Doe", total = 1109.97m, status = "completed", order_date = DateTime.UtcNow, last_synced = DateTime.UtcNow, sync_status = "success", shipping_cost = 10.00m, tax = 89.97m },
                new { id = 2, customer_name = "Jane Smith", total = 79.99m, status = "pending", order_date = DateTime.UtcNow, last_synced = DateTime.UtcNow, sync_status = "pending", shipping_cost = 5.00m, tax = 6.40m }
            };

            return Results.Ok(orders);
        })
        .WithName("GetBackupOrders")
        .WithTags("UI API");

        // Backup Categories endpoints
        endpoints.MapGet("/api/backup/categories", async () =>
        {
            // Return mock backup data
            var categories = new[]
            {
                new { id = 1, name = "Electronics", description = "Electronic devices and accessories", product_count = 15, last_synced = DateTime.UtcNow, sync_status = "success" },
                new { id = 2, name = "Books", description = "Books and publications", product_count = 45, last_synced = DateTime.UtcNow, sync_status = "success" }
            };

            return Results.Ok(categories);
        })
        .WithName("GetBackupCategories")
        .WithTags("UI API");

        return endpoints;
    }
}