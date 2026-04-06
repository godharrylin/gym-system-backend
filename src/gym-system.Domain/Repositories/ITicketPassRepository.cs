using gym_system.Domain.Entities.Tickets;

namespace gym_system.Domain.Repositories
{
    //  處理學生票券
    public interface ITicketPassRepository
    {
        Task AddRangeAsync(IReadOnlyList<TicketPass> passes, CancellationToken ct);
    }
}
