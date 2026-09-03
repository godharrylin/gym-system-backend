namespace gym_system.Api.Contracts.Orders
{
    public sealed class CreateOrderRequest
    {
        public string BuyerId { get; init; } = string.Empty;
        public string PaymentStatus { get; init; } = string.Empty;
        public string? PaymentMethod { get; init; }
        public TicketPurchaseOrderRequest TicketPurchase { get; init; } = new();
    }

    public sealed class TicketPurchaseOrderRequest
    {
        public string TicketPlanKindCode { get; init; } = string.Empty;
        public IReadOnlyList<string> BeneficiaryStudentIds { get; init; } = Array.Empty<string>();
        public int Quantity { get; init; } = 1;
    }
}

