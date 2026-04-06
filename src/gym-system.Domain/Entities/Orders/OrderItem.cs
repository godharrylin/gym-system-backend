namespace gym_system.Domain.Entities.Orders
{
    public sealed class OrderItem
    {
        private OrderItem(
            string id,
            string orderId,
            OrderItemType type,
            string refId,
            decimal unitPrice,
            decimal totalAmount,
            decimal actualAmount,
            UnitType quantityUnit,
            int quantity,
            int bonusQuantity,
            OrderItemPaymentMethod paymentMethod,
            OrderItemPaymentState paymentState,
            DateTime buyAt)
        {
            Id = id;
            OrderId = orderId;
            Type = type;
            RefId = refId;
            UnitPrice = unitPrice;
            TotalAmount = totalAmount;
            ActualAmount = actualAmount;
            QuantityUnit = quantityUnit;
            Quantity = quantity;
            BonusQuantity = bonusQuantity;
            PaymentMethod = paymentMethod;
            PaymentState = paymentState;
            BuyAt = buyAt;
        }

        /// <summary>
        /// 訂單明細流水號
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// 訂單流水號
        /// </summary>
        public string OrderId { get; }
        /// <summary>
        /// 訂單類別
        /// </summary>
        public OrderItemType Type { get; }
        /// <summary>
        /// 訂單品項識別碼，用於識別訂單中實際購買之票券或產品的唯一識別碼
        /// </summary>
        public string RefId {  get; }
        public OrderItemPaymentMethod PaymentMethod { get; }
        /// <summary>
        /// 單價
        /// </summary>
        public decimal UnitPrice {  get; }
        /// <summary>
        /// 應付金額
        /// </summary>
        public decimal TotalAmount { get; }
        /// <summary>
        /// 實收金額
        /// </summary>
        public decimal ActualAmount { get; }
        /// <summary>
        /// 數量單位種類
        /// </summary>
        public UnitType QuantityUnit { get; }
        /// <summary>
        /// 購買數量
        ///• 月票 → 單位是天
        ///• 堂票 → 單位是堂
        ///• 商品 → 單位是個
        /// </summary>
        public int Quantity { get; }
        /// <summary>
        /// 贈送數量
        ///• 月票 → 單位是天
        ///• 堂票 → 單位是堂
        ///• 商品 → 單位是個
        /// </summary>
        public int BonusQuantity { get; }
        /// <summary>
        /// 總數量 (業務邏輯需要，不需映射到資料庫)
        /// </summary>
        public int TotalQuantity => Quantity + BonusQuantity;
        public OrderItemPaymentState PaymentState { get; }
        public DateTime BuyAt { get; }

        /// <summary>
        /// 建立票券訂單明細
        /// </summary>
        /// <param name="id"></param>
        /// <param name="orderId"></param>
        /// <param name="ticketPlanKindId"></param>
        /// <param name="unitPrice"></param>
        /// <param name="totalAmount"></param>
        /// <param name="actualAmount"></param>
        /// <param name="quantity"></param>
        /// <param name="bonusQuantity"></param>
        /// <param name="paymentState"></param>
        /// <param name="buyAt"></param>
        /// <returns></returns>
        public static OrderItem CreateTicketItem(
            string id,
            string orderId,
            string ticketPlanKindId,
            decimal unitPrice,
            decimal totalAmount,
            decimal actualAmount,
            UnitType quantityUnit,
            int quantity,
            int bonusQuantity,
            OrderItemPaymentMethod paymentMethod,
            OrderItemPaymentState paymentState,
            DateTime buyAt)
        {
            return new OrderItem(
                id, orderId, OrderItemType.Ticket, ticketPlanKindId, 
                unitPrice, totalAmount, actualAmount, quantityUnit, quantity, 
                bonusQuantity, paymentMethod, paymentState, buyAt);
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
