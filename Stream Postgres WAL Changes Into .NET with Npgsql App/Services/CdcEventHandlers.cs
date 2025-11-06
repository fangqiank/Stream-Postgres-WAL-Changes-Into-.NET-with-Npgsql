using Microsoft.Extensions.Options;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Configuration;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Data;
using Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Models;
using System.Text.Json;

namespace Stream_Postgres_WAL_Changes_Into_.NET_with_Npgsql_App.Services
{
    /// <summary>
    /// CDC事件处理器工厂
    /// </summary>
    public interface ICdcEventHandler
    {
        Task HandleAsync(ChangeEvent changeEvent, CancellationToken cancellationToken = default);
        bool CanHandle(string tableName);
    }

    /// <summary>
    /// 订单变更处理器
    /// </summary>
    public class OrderChangeEventHandler : ICdcEventHandler
    {
        private readonly ILogger<OrderChangeEventHandler> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RealTimeNotificationService _notificationService;

        public OrderChangeEventHandler(
            ILogger<OrderChangeEventHandler> logger,
            IServiceProvider serviceProvider,
            RealTimeNotificationService notificationService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _notificationService = notificationService;
        }

        public bool CanHandle(string tableName)
        {
            return tableName.Equals("orders", StringComparison.OrdinalIgnoreCase);
        }

        public async Task HandleAsync(ChangeEvent changeEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Processing order change event: {EventType} for OrderId: {OrderId}",
                    changeEvent.EventType,
                    GetOrderId(changeEvent));

                switch (changeEvent.EventType)
                {
                    case ChangeEventType.Insert:
                        await HandleOrderInsertAsync(changeEvent, cancellationToken);
                        break;

                    case ChangeEventType.Update:
                        await HandleOrderUpdateAsync(changeEvent, cancellationToken);
                        break;

                    case ChangeEventType.Delete:
                        await HandleOrderDeleteAsync(changeEvent, cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling order change event");
            }

            await Task.CompletedTask;
        }

        private async Task HandleOrderInsertAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            var orderData = changeEvent.GetAfterData<Order>();
            if (orderData == null) return;

            _logger.LogInformation("New order created: OrderId={OrderId}, Amount={Amount}, Status={Status}",
                orderData.Id, orderData.Amount, orderData.Status);

            // 发送实时通知到前端
            await _notificationService.BroadcastOrderCreatedAsync(orderData);

            // 处理订单插入的业务逻辑
            await ProcessOrderCreatedAsync(orderData, cancellationToken);
        }

        private async Task HandleOrderUpdateAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            var beforeData = changeEvent.GetBeforeData<Order>();
            var afterData = changeEvent.GetAfterData<Order>();

            if (beforeData?.Status != afterData?.Status)
            {
                _logger.LogInformation("Order status changed: OrderId={OrderId}, From={FromStatus}, To={ToStatus}",
                    afterData?.Id, beforeData?.Status, afterData?.Status);

                // 发送实时通知到前端
                await _notificationService.BroadcastOrderStatusChangeAsync(
                    afterData!.Id,
                    beforeData!.Status!,
                    afterData.Status!);

                // 处理状态变更逻辑
                await ProcessOrderStatusChangedAsync(beforeData, afterData, cancellationToken);
            }

            if (changeEvent.ChangedColumns.Contains("amount"))
            {
                _logger.LogInformation("Order amount updated: OrderId={OrderId}, OldAmount={OldAmount}, NewAmount={NewAmount}",
                    afterData?.Id, beforeData?.Amount, afterData?.Amount);

                // 发送订单更新通知到前端
                var changedFields = new[] { "amount" };
                await _notificationService.BroadcastOrderUpdatedAsync(afterData!, changedFields);

                // 处理金额更新逻辑
                await ProcessOrderAmountUpdatedAsync(beforeData!, afterData!, cancellationToken);
            }
        }

        private async Task HandleOrderDeleteAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            var orderData = changeEvent.GetBeforeData<Order>();
            if (orderData == null) return;

            _logger.LogInformation("Order deleted: OrderId={OrderId}, Status={Status}",
                orderData.Id, orderData.Status);

            // 处理订单删除的业务逻辑
            await ProcessOrderDeletedAsync(orderData, cancellationToken);
        }

        private async Task ProcessOrderCreatedAsync(Order order, CancellationToken cancellationToken)
        {
            // 发送确认邮件
            await SendOrderConfirmationEmailAsync(order, cancellationToken);

            // 更新库存
            await UpdateInventoryAsync(order, cancellationToken);

            // 发送通知
            await SendNotificationAsync($"Order {order.Id} created successfully", cancellationToken);
        }

        private async Task ProcessOrderStatusChangedAsync(Order before, Order after, CancellationToken cancellationToken)
        {
            switch (after.Status.ToLowerInvariant())
            {
                case "confirmed":
                    await ProcessOrderConfirmedAsync(after, cancellationToken);
                    break;
                case "shipped":
                    await ProcessOrderShippedAsync(after, cancellationToken);
                    break;
                case "delivered":
                    await ProcessOrderDeliveredAsync(after, cancellationToken);
                    break;
                case "cancelled":
                    await ProcessOrderCancelledAsync(before, after, cancellationToken);
                    break;
            }
        }

        private async Task ProcessOrderAmountUpdatedAsync(Order before, Order after, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Order amount updated: OrderId={OrderId}, Difference={Difference}",
                after.Id, after.Amount - before.Amount);

            // 处理金额变更相关的业务逻辑
            await SendNotificationAsync($"Order {after.Id} amount updated", cancellationToken);
        }

        private async Task ProcessOrderDeletedAsync(Order order, CancellationToken cancellationToken)
        {
            // 恢复库存
            await RestoreInventoryAsync(order, cancellationToken);

            // 记录删除原因
            await LogOrderDeletionAsync(order, cancellationToken);
        }

        private async Task ProcessOrderConfirmedAsync(Order order, CancellationToken cancellationToken)
        {
            // 确认库存
            await ReserveInventoryAsync(order, cancellationToken);

            // 准备发货
            await PrepareShipmentAsync(order, cancellationToken);
        }

        private async Task ProcessOrderShippedAsync(Order order, CancellationToken cancellationToken)
        {
            // 更新追踪信息
            await UpdateTrackingInfoAsync(order, cancellationToken);

            // 发送发货通知
            await SendShipmentNotificationAsync(order, cancellationToken);
        }

        private async Task ProcessOrderDeliveredAsync(Order order, CancellationToken cancellationToken)
        {
            // 完成订单
            await CompleteOrderAsync(order, cancellationToken);

            // 发送满意度调查
            await SendSatisfactionSurveyAsync(order, cancellationToken);
        }

        private async Task ProcessOrderCancelledAsync(Order before, Order after, CancellationToken cancellationToken)
        {
            // 释放库存
            await ReleaseInventoryAsync(before, cancellationToken);

            // 处理退款
            await ProcessRefundAsync(before, cancellationToken);
        }

        // Helper methods
        private async Task SendOrderConfirmationEmailAsync(Order order, CancellationToken cancellationToken)
        {
            // 实现邮件发送逻辑
            _logger.LogDebug("Sending confirmation email for order: {OrderId}", order.Id);
            await Task.Delay(100, cancellationToken); // 模拟邮件发送
        }

        private async Task UpdateInventoryAsync(Order order, CancellationToken cancellationToken)
        {
            // 实现库存更新逻辑
            _logger.LogDebug("Updating inventory for order: {OrderId}", order.Id);
            await Task.Delay(50, cancellationToken);
        }

        private async Task SendNotificationAsync(string message, CancellationToken cancellationToken)
        {
            // 实现通知发送逻辑
            _logger.LogDebug("Sending notification: {Message}", message);
            await Task.Delay(50, cancellationToken);
        }

        private async Task ReserveInventoryAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Reserving inventory for order: {OrderId}", order.Id);
            await Task.Delay(50, cancellationToken);
        }

        private async Task PrepareShipmentAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Preparing shipment for order: {OrderId}", order.Id);
            await Task.Delay(100, cancellationToken);
        }

        private async Task UpdateTrackingInfoAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Updating tracking info for order: {OrderId}", order.Id);
            await Task.Delay(50, cancellationToken);
        }

        private async Task SendShipmentNotificationAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Sending shipment notification for order: {OrderId}", order.Id);
            await Task.Delay(100, cancellationToken);
        }

        private async Task CompleteOrderAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Completing order: {OrderId}", order.Id);
            await Task.Delay(50, cancellationToken);
        }

        private async Task SendSatisfactionSurveyAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Sending satisfaction survey for order: {OrderId}", order.Id);
            await Task.Delay(100, cancellationToken);
        }

        private async Task ReleaseInventoryAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Releasing inventory for order: {OrderId}", order.Id);
            await Task.Delay(50, cancellationToken);
        }

        private async Task ProcessRefundAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Processing refund for order: {OrderId}", order.Id);
            await Task.Delay(200, cancellationToken);
        }

        private async Task RestoreInventoryAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Restoring inventory for order: {OrderId}", order.Id);
            await Task.Delay(50, cancellationToken);
        }

        private async Task LogOrderDeletionAsync(Order order, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Logging order deletion: {OrderId}", order.Id);
            await Task.Delay(50, cancellationToken);
        }

        private string? GetOrderId(ChangeEvent changeEvent)
        {
            try
            {
                if (changeEvent.EventType == ChangeEventType.Insert || changeEvent.EventType == ChangeEventType.Update)
                {
                    var data = changeEvent.GetAfterData<Dictionary<string, object>>();
                    return data?.GetValueOrDefault("id")?.ToString();
                }
                else if (changeEvent.EventType == ChangeEventType.Delete)
                {
                    var data = changeEvent.GetBeforeData<Dictionary<string, object>>();
                    return data?.GetValueOrDefault("id")?.ToString();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Outbox事件处理器
    /// </summary>
    public class OutboxEventHandler : ICdcEventHandler
    {
        private readonly ILogger<OutboxEventHandler> _logger;
        private readonly IServiceProvider _serviceProvider;

        public OutboxEventHandler(ILogger<OutboxEventHandler> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public bool CanHandle(string tableName)
        {
            return tableName.Equals("outboxevents", StringComparison.OrdinalIgnoreCase);
        }

        public async Task HandleAsync(ChangeEvent changeEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Processing outbox event: {EventType}", changeEvent.EventType);

                // Outbox事件通常是已处理的事件被删除，或者是新事件被插入
                if (changeEvent.EventType == ChangeEventType.Insert)
                {
                    await ProcessNewOutboxEventAsync(changeEvent, cancellationToken);
                }
                else if (changeEvent.EventType == ChangeEventType.Delete)
                {
                    await ProcessOutboxEventDeletionAsync(changeEvent, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling outbox event");
            }

            await Task.CompletedTask;
        }

        private async Task ProcessNewOutboxEventAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            var eventData = changeEvent.GetAfterData<OutboxEvent>();
            if (eventData == null) return;

            _logger.LogInformation("New outbox event detected: EventId={EventId}, EventType={EventType}, AggregateType={AggregateType}",
                eventData.Id, eventData.EventType, eventData.AggregateType);

            // 这里可以添加具体的业务逻辑，比如：
            // - 发送外部通知
            // - 更新缓存
            // - 触发其他系统的事件
        }

        private async Task ProcessOutboxEventDeletionAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            var eventData = changeEvent.GetBeforeData<OutboxEvent>();
            if (eventData == null) return;

            _logger.LogInformation("Outbox event processed and deleted: EventId={EventId}, EventType={EventType}",
                eventData.Id, eventData.EventType);

            // 记录事件处理完成
            await RecordEventProcessingAsync(eventData, cancellationToken);
        }

        private async Task RecordEventProcessingAsync(OutboxEvent eventData, CancellationToken cancellationToken)
        {
            // 实现事件处理记录逻辑
            _logger.LogDebug("Recording processed event: {EventId}", eventData.Id);
            await Task.Delay(50, cancellationToken);
        }
    }

    /// <summary>
    /// 通用变更事件处理器
    /// </summary>
    public class GenericChangeEventHandler : ICdcEventHandler
    {
        private readonly ILogger<GenericChangeEventHandler> _logger;

        public GenericChangeEventHandler(ILogger<GenericChangeEventHandler> logger)
        {
            _logger = logger;
        }

        public bool CanHandle(string tableName)
        {
            // 处理所有表，除非有更具体的处理器
            return true;
        }

        public async Task HandleAsync(ChangeEvent changeEvent, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Generic CDC event: {EventType} on {Table} at {Time}",
                changeEvent.EventType, changeEvent.TableName, changeEvent.EventTime);

            // 记录所有变更到日志
            await LogGenericChangeAsync(changeEvent, cancellationToken);

            await Task.CompletedTask;
        }

        private async Task LogGenericChangeAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            var logEntry = new
            {
                EventType = changeEvent.EventType.ToString(),
                TableName = changeEvent.TableName,
                SchemaName = changeEvent.SchemaName,
                EventTime = changeEvent.EventTime,
                TransactionId = changeEvent.TransactionId,
                Lsn = changeEvent.Lsn,
                ChangedColumns = changeEvent.ChangedColumns,
                BeforeDataExists = !string.IsNullOrEmpty(changeEvent.BeforeData),
                AfterDataExists = !string.IsNullOrEmpty(changeEvent.AfterData)
            };

            _logger.LogDebug("Generic change details: {@ChangeDetails}", logEntry);
            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>
    /// CDC事件处理器管理器
    /// </summary>
    public class CdcEventHandlerManager
    {
        private readonly ILogger<CdcEventHandlerManager> _logger;
        private readonly List<ICdcEventHandler> _handlers;

        public CdcEventHandlerManager(
            ILogger<CdcEventHandlerManager> logger,
            IEnumerable<ICdcEventHandler> handlers)
        {
            _logger = logger;
            _handlers = handlers.OrderBy(h => h is GenericChangeEventHandler ? 1 : 0).ToList();
        }

        public async Task HandleEventAsync(ChangeEvent changeEvent, CancellationToken cancellationToken = default)
        {
            var capableHandlers = _handlers.Where(h => h.CanHandle(changeEvent.TableName)).ToList();

            if (!capableHandlers.Any())
            {
                _logger.LogWarning("No handler found for table: {TableName}", changeEvent.TableName);
                return;
            }

            _logger.LogDebug("Found {Count} handlers for table: {TableName}",
                capableHandlers.Count, changeEvent.TableName);

            var tasks = capableHandlers.Select(handler =>
            {
                try
                {
                    return handler.HandleAsync(changeEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in handler {HandlerType} for table {TableName}",
                        handler.GetType().Name, changeEvent.TableName);
                    return Task.CompletedTask;
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}