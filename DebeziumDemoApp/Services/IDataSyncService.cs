using DebeziumDemoApp.Models;

namespace DebeziumDemoApp.Services;

public interface IDataSyncService
{
    Task SyncChangeToBackupAsync(DatabaseChangeNotification change);
    Task<Product> SyncProductAsync(Product product, string operation);
    Task<Order> SyncOrderAsync(Order order, string operation);
    Task<Category> SyncCategoryAsync(Category category, string operation);
}