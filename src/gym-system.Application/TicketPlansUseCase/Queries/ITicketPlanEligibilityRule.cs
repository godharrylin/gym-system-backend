namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public interface ITicketPlanEligibilityRule
    {
        //  是否將規則套用到票券上
        bool AppliesTo(TicketPlanResult ticketPlan);

        //  是否滿足該規則
        Task<bool> IsSatisfiedAsync(
            StudentTicketPlanEligibilityContext context,
            TicketPlanResult ticketPlan,
            CancellationToken ct);
    }
}
