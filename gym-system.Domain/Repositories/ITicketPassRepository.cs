using gym_system.Domain.Entities.Tickets;

namespace gym_system.Domain.Repositories
{
    public interface ITicketPassRepository
    {
        Task AddRangeAsync(IReadOnlyList<TicketPass> passes, CancellationToken ct);
    }
}
