namespace gym_system.Application.OrdersUseCase.Commands.CreateOrder
{
    public sealed class CreateOrderResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? OrderId { get; init; }
    }
}
