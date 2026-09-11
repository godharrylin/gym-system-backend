namespace gym_system.Application.TicketPlansUseCase.Queries
{
    //  驗證票券
    public interface ITicketPlanEligibilityService
    {
        Task<StudentTicketPlanEligibilityContext?> GetEligibilityContextAsync(
            string studentId,
            CancellationToken ct);

        StudentTicketPlanEligibilityContext CreateRegistrationContext();

        Task<bool> CanPurchaseAsync(
            StudentTicketPlanEligibilityContext context,
            TicketPlanResult ticketPlan,
            CancellationToken ct);
    }
}
