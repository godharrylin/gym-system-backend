namespace gym_system.Domain.Entities.Orders
{
    public sealed class Order
    {
        private readonly List<OrderItem> _items = [];

        private Order(
            string id,
            string buyerId,
            string buyerName,
            decimal totalAmount,
            decimal actualAmount,
            OrderOverallPaymentState paymentState,
            string operatorId,
            DateTime buyAt)
        {
            Id = id;
            BuyerId = buyerId;
            BuyerName = buyerName;
            TotalAmount = totalAmount;
            ActualAmount = actualAmount;
            PaymentState = paymentState;
            OperatorId = operatorId;
            BuyAt = buyAt;
        }

        public string Id { get; }
        public string BuyerId { get; }
        public string BuyerName { get; }
        public decimal TotalAmount { get; }
        public decimal ActualAmount { get; }
        public OrderOverallPaymentState PaymentState { get; private set; }
        public string OperatorId { get; }
        public DateTime BuyAt { get; }
        public IReadOnlyList<OrderItem> Items => _items;

        public static Order Create(
            string id,
            string buyerId,
            string buyerName,
            decimal totalAmount,
            decimal actualAmount,
            OrderOverallPaymentState paymentState,
            string operatorId,
            DateTime buyAt)
        {
            if (string.IsNullOrWhiteSpace(buyerName))
            {
                throw new InvalidOperationException("購買者姓名必填");
            }

            return new Order(
                id,
                buyerId,
                buyerName.Trim(),
                totalAmount,
                actualAmount,
                paymentState,
                operatorId,
                buyAt);
        }

        public void AddItem(OrderItem item)
        {
            _items.Add(item);
        }

        public void MarkPaid()
        {
            if (PaymentState != OrderOverallPaymentState.UnPaid)
            {
                throw new InvalidOperationException("只有未付款訂單可以付款");
            }

            PaymentState = OrderOverallPaymentState.Paid;
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
