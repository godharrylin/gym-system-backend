using gym_system.Domain.Entities.Tickets;

namespace gym_system.Domain.Repositories
{
    public interface ITicketPlanRepository
    {
        Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct);
    }
}
