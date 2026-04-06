using gym_system.Domain.Entities.Tickets;

namespace gym_system.Domain.Repositories
{
    //  處理票券相關資訊
    public interface ITicketPlanRepository
    {
        /// <summary>
        /// 用ID查詢可購買票券
        /// </summary>
        /// <param name="ticketPlanKindId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct);
    }
}
