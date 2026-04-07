
namespace gym_system.Application.TicketPlansUseCase.Queries
{
    //  查詢顯示可購買票券清單
    public interface ITicketPlanCatalogQuerySerivce
    {
        Task<IReadOnlyList<TicketPlanResult>> GetActiveTicketPlansAsync(CancellationToken ct);
    }
}
