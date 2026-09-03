using gym_system.Domain.Enums;

namespace gym_system.Application.OrdersUseCase.Commands.CreateOrder
{
    public sealed class CreateOrderCommand
    {
        public string BuyerId { get; init; } = string.Empty;
        public PaymentState PaymentStatus { get; init; }
        public string? PaymentMethod { get; init; }
        public TicketPurchaseOrderInput TicketPurchase { get; init; } = new();
        public string OperatorId { get; init; } = "ADMIN_PLACEHOLDER";
    }

    public sealed class TicketPurchaseOrderInput
    {
        public string TicketPlanKindCode { get; init; } = string.Empty;
        public IReadOnlyList<string> BeneficiaryStudentIds { get; init; } = Array.Empty<string>();
        public int Quantity { get; init; } = 1;
    }
}

