namespace gym_system.Domain.Entities.Orders
{
    //  訂單明細
    public sealed class OrderItem
    {
        private OrderItem(
            string id,
            string orderId,
            OrderItemType type,
            string refId,
            string name,
            decimal unitPrice,
            decimal totalAmount,
            decimal actualAmount,
            UnitType quantityUnit,
            int quantity,
            int bonusQuantity,
            UnitType? bonusUnit,
            string? discountType,
            decimal? discountRate,
            OrderItemPaymentMethod paymentMethod,
            OrderItemPaymentState paymentState,
            DateTime buyAt)
        {
            Id = id;
            OrderId = orderId;
            Type = type;
            RefId = refId;
            Name = name;
            UnitPrice = unitPrice;
            TotalAmount = totalAmount;
            ActualAmount = actualAmount;
            QuantityUnit = quantityUnit;
            Quantity = quantity;
            BonusQuantity = bonusQuantity;
            BonusUnit = bonusUnit;
            DiscountType = discountType;
            DiscountRate = discountRate;
            PaymentMethod = paymentMethod;
            PaymentState = paymentState;
            BuyAt = buyAt;
            PaidAt = paymentState == OrderItemPaymentState.Paid ? buyAt : null;
        }

        public string Id { get; }
        public string OrderId { get; }
        public OrderItemType Type { get; }
        public string RefId { get; }
        public string Name { get; }
        public OrderItemPaymentMethod PaymentMethod { get; }
        public decimal UnitPrice { get; }
        public decimal TotalAmount { get; }
        public decimal ActualAmount { get; }
        public UnitType QuantityUnit { get; }
        public int Quantity { get; }
        public int BonusQuantity { get; }
        public UnitType? BonusUnit { get; }
        public string? DiscountType { get; }
        public decimal? DiscountRate { get; }
        public int TotalQuantity => Quantity;
        public OrderItemPaymentState PaymentState { get; private set; }
        public DateTime BuyAt { get; }
        public DateTime? PaidAt { get; private set; }

        public static OrderItem CreateTicketItem(
            string id,
            string orderId,
            string ticketPlanKindId,
            string ticketPlanKindName,
            decimal unitPrice,
            decimal totalAmount,
            decimal actualAmount,
            UnitType quantityUnit,
            int quantity,
            int bonusQuantity,
            UnitType? bonusUnit,
            string? discountType,
            decimal? discountRate,
            OrderItemPaymentMethod paymentMethod,
            OrderItemPaymentState paymentState,
            DateTime buyAt)
        {
            return new OrderItem(
                id,
                orderId,
                OrderItemType.Ticket,
                ticketPlanKindId,
                ticketPlanKindName,
                unitPrice,
                totalAmount,
                actualAmount,
                quantityUnit,
                quantity,
                bonusQuantity,
                bonusUnit,
                discountType,
                discountRate,
                paymentMethod,
                paymentState,
                buyAt);
        }

        public void MarkPaid(DateTime paidAt)
        {
            if (PaymentState != OrderItemPaymentState.UnPaid)
            {
                throw new InvalidOperationException("只有未付款訂單明細可以付款");
            }

            PaymentState = OrderItemPaymentState.Paid;
            PaidAt = paidAt;
        }
    }

    public enum OrderItemType
    {
        Ticket,
        Product
    }

    public enum OrderItemPaymentState
    {
        Paid = 1,
        UnPaid = 2,
        Cancel = 3
    }

    public enum OrderItemPaymentMethod
    {
        Cash
    }

    public enum UnitType
    {
        Days,
        Credits,
        Pieces
    }
}
