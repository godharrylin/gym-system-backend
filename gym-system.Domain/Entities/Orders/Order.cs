namespace gym_system.Domain.Entities.Orders
{
    public sealed class Order
    {
        private readonly List<OrderItem> _items = [];

        private Order(
            string id,
            string buyerId,
            decimal totalAmount,
            decimal actualAmount,
            OrderOverallPaymentState paymentState,
            string operatorId,
            DateTime buyAt)
        {
            Id = id;
            BuyerId = buyerId;
            TotalAmount = totalAmount;
            ActualAmount = actualAmount;
            PaymentState = paymentState;
            OperatorId = operatorId;
            BuyAt = buyAt;
        }

        public string Id { get; }
        public string BuyerId { get; }
        public decimal TotalAmount { get; }
        public decimal ActualAmount { get; }
        public OrderOverallPaymentState PaymentState { get; }
        public string OperatorId { get; }
        public DateTime BuyAt { get; }
        public IReadOnlyList<OrderItem> Items => _items;

        public static Order Create(
            string id,
            string buyerId,
            decimal totalAmount,
            decimal actualAmount,
            OrderOverallPaymentState paymentState,
            string operatorId,
            DateTime buyAt)
        {
            return new Order(id, buyerId, totalAmount, actualAmount, paymentState, operatorId, buyAt);
        }

        public void AddItem(OrderItem item)
        {
            _items.Add(item);
        }
    }

    public enum OrderOverallPaymentState
    {
        Paid = 1,
        PartialPaid = 2,
        UnPaid = 3,
        Cancel = 4
    }
}
