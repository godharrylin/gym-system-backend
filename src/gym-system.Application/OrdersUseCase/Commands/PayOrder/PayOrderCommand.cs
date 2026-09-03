namespace gym_system.Application.OrdersUseCase.Commands.PayOrder
{
    public sealed class PayOrderCommand
    {
        public string OrderId { get; init; } = string.Empty;
        public string PaymentMethod { get; init; } = "Cash";
        public string OperatorId { get; init; } = "ADMIN_PLACEHOLDER";
    }
}
