namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class NewOnlyTicketPlanEligibilityRule : ITicketPlanEligibilityRule
    {
        private const string NewOnlyRuleCode = "NEW_ONLY";
        private const int NewStudentEligibleDays = 30;
        private readonly IStudentTicketPurchaseHistoryQueryService _purchaseHistoryQueryService;

        public NewOnlyTicketPlanEligibilityRule(
            IStudentTicketPurchaseHistoryQueryService purchaseHistoryQueryService)
        {
            _purchaseHistoryQueryService = purchaseHistoryQueryService;
        }

        public bool AppliesTo(TicketPlanResult ticketPlan)
        {
            return ticketPlan.EligibilityRuleCodes.Contains(
                NewOnlyRuleCode,
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task<bool> IsSatisfiedAsync(
            StudentTicketPlanEligibilityContext context,
            TicketPlanResult ticketPlan,
            CancellationToken ct)
        {
            if (!context.StudentAssignedAt.HasValue)
            {
                return false;
            }

            //  判斷是否新會員
            var eligibleSince = context.Now.AddDays(-NewStudentEligibleDays);
            if (context.StudentAssignedAt.Value < eligibleSince)
            {
                return false;
            }

            //  判斷是否買過此方案
            var hasPurchased = await _purchaseHistoryQueryService.HasPurchasedTicketPlanAsync(
                context.StudentId,
                ticketPlan.Id,
                ct);

            return !hasPurchased;
        }
    }
}