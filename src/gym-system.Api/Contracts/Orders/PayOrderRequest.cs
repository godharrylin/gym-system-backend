namespace gym_system.Api.Contracts.Orders
{
    public sealed class PayOrderRequest
    {
        public string PaymentMethod { get; init; } = "Cash";
    }
}
