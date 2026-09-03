namespace gym_system.Api.Contracts.Orders
{
    public sealed class CreateOrderResponse
    {
        public bool Success { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public string? OrderId { get; init; }
    }
}
