using gym_system.Domain.Entities.Orders;

namespace gym_system.Domain.Repositories
{
    public interface IOrderRepository
    {
        Task AddAsync(Order order, CancellationToken ct);
    }
}
