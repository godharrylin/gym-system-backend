namespace gym_system.Application.TicketPlansUseCase.Queries
{
    public sealed class GetPurchasableTicketPlansForStudentHandler
    {
        private readonly ITicketPlanCatalogQueryService _ticketPlanCatalogQueryService;
        private readonly ITicketPlanEligibilityService _ticketPlanEligibilityService;

        public GetPurchasableTicketPlansForStudentHandler(
            ITicketPlanCatalogQueryService ticketPlanCatalogQueryService,
            ITicketPlanEligibilityService ticketPlanEligibilityService)
        {
            _ticketPlanCatalogQueryService = ticketPlanCatalogQueryService;
            _ticketPlanEligibilityService = ticketPlanEligibilityService;
        }

        public async Task<IReadOnlyList<TicketPlanResult>> Handle(string studentId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(studentId))
            {
                throw new InvalidOperationException("學生 ID 必填");
            }

            var ticketPlans = await _ticketPlanCatalogQueryService.GetActiveTicketPlansAsync(ct);
            var purchasablePlans = new List<TicketPlanResult>();

            foreach (var ticketPlan in ticketPlans)
            {
                if (await _ticketPlanEligibilityService.CanPurchaseAsync(studentId, ticketPlan, ct))
                {
                    purchasablePlans.Add(ticketPlan);
                }
            }

            return purchasablePlans;
        }
    }
}
