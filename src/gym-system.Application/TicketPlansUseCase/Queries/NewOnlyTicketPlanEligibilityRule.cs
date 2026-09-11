namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class NewOnlyTicketPlanEligibilityRule : ITicketPlanEligibilityRule
    {
        private const string NewOnlyRuleCode = TicketPlanRulePolicy.NewOnly;
        private const int NewStudentEligibleDaysIncludingAssignedDate = 30;
        private readonly IStudentTicketPurchaseHistoryQueryService _purchaseHistoryQueryService;
        public string RuleCode => NewOnlyRuleCode;

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

            var today = DateOnly.FromDateTime(context.Now);
            var assignedDate = DateOnly.FromDateTime(context.StudentAssignedAt.Value);
            var earliestEligibleDate = today.AddDays(
                -(NewStudentEligibleDaysIncludingAssignedDate - 1));
            if (assignedDate < earliestEligibleDate)
            {
                return false;
            }

            //  判斷是否買過此方案
            var hasPurchased = await _purchaseHistoryQueryService.HasPurchasedTicketPlanAsync(
                context.StudentId!,
                ticketPlan.Id,
                ct);

            return !hasPurchased;
        }
    }
}
