using gym_system.Domain.Entities.Orders;

namespace gym_system.Domain.Repositories
{
    public interface IOrderRepository
    {
        Task<OrderPersistenceResult> AddAsync(Order order, CancellationToken ct);

        Task<UnpaidTicketOrder?> FindUnpaidTicketOrderAsync(
            string orderId,
            bool acquireLock,
            CancellationToken ct);

        Task MarkPaidAsync(
            int orderSn,
            int orderItemSn,
            DateTime paidAt,
            string paymentMethod,
            string operatorId,
            CancellationToken ct);
    }

    public sealed class OrderPersistenceResult
    {
        public required int OrderSn { get; init; }
        public required string OrderId { get; init; }
        public IReadOnlyList<OrderItemPersistenceResult> Items { get; init; } = Array.Empty<OrderItemPersistenceResult>();
    }

    public sealed class OrderItemPersistenceResult
    {
        public required string ClientItemId { get; init; }
        public required int OrderItemSn { get; init; }
        public required string OrderItemId { get; init; }
    }

    public sealed class UnpaidTicketOrder
    {
        public required int OrderSn { get; init; }
        public required string OrderId { get; init; }
        public required string BuyerId { get; init; }
        public required int OrderItemSn { get; init; }
        public required string OrderItemId { get; init; }
        public required string TicketPlanKindCode { get; init; }
        public required int Quantity { get; init; }
        public required decimal UnitPrice { get; init; }
        public required decimal TotalAmount { get; init; }
        public required decimal ActualAmount { get; init; }
    }
}
