namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class RenewalTicketPlanEligibilityRule : ITicketPlanEligibilityRule
    {
        private const string RenewalRuleCode = "RENEWAL";
        private readonly RenewalTicketPassEligibilityService _renewalEligibilityService;
        public string RuleCode => RenewalRuleCode;

        public RenewalTicketPlanEligibilityRule(
            RenewalTicketPassEligibilityService renewalEligibilityService)
        {
            _renewalEligibilityService = renewalEligibilityService;
        }

        public bool AppliesTo(TicketPlanResult ticketPlan)
        {
            return ticketPlan.EligibilityRuleCodes.Contains(
                RenewalRuleCode,
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task<bool> IsSatisfiedAsync(
            StudentTicketPlanEligibilityContext context,
            TicketPlanResult ticketPlan,
            CancellationToken ct)
        {
            return await _renewalEligibilityService.FindEligibleSourceAsync(
                context.StudentId,
                ticketPlan.FamilyCode,
                context.Now,
                acquireLock: false,
                ct) is not null;
        }
    }
}
