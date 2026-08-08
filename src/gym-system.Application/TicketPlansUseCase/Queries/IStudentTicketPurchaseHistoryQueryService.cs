namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public interface IStudentTicketPurchaseHistoryQueryService
    {
        Task<bool> HasPurchasedTicketPlanAsync(
            string studentId,
            string ticketPlanCode,
            CancellationToken ct);
    }
}
