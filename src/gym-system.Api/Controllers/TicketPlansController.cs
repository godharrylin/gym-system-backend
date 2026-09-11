using gym_system.Api.Contracts.TicketPlans;
using gym_system.Application.TicketPlansUseCase.Queries;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/ticket-plans")]

    public class TicketPlansController : ControllerBase
    {
        private readonly ITicketPlanCatalogQueryService _ticketPlanQueryService;
        private readonly ITicketPlanEligibilityService _ticketPlanEligibilityService;

        public TicketPlansController(
            ITicketPlanCatalogQueryService ticketPlanQueryService,
            ITicketPlanEligibilityService ticketPlanEligibilityService)
        {
            _ticketPlanQueryService = ticketPlanQueryService;
            _ticketPlanEligibilityService = ticketPlanEligibilityService;
        }

        [HttpGet]
        public async Task<ActionResult<GetTicketPlansResponse>> GetActiveTicketPlansAsync(CancellationToken ct)
        {
            IReadOnlyList<TicketPlanResult> result = await _ticketPlanQueryService.GetActiveTicketPlansAsync(ct);

            return Ok(MapResponse(result));
        }

        [HttpGet("registration-purchasable")]
        public async Task<ActionResult<GetTicketPlansResponse>> GetRegistrationPurchasableAsync(
            CancellationToken ct)
        {
            var result = await _ticketPlanQueryService.GetActiveTicketPlansAsync(ct);
            var context = _ticketPlanEligibilityService.CreateRegistrationContext();
            var registrationPlans = new List<TicketPlanResult>();
            foreach (var ticketPlan in result)
            {
                if (await _ticketPlanEligibilityService.CanPurchaseAsync(
                    context,
                    ticketPlan,
                    ct))
                {
                    registrationPlans.Add(ticketPlan);
                }
            }

            return Ok(MapResponse(registrationPlans));
        }

        private static GetTicketPlansResponse MapResponse(
            IReadOnlyList<TicketPlanResult> result)
        {
            return new GetTicketPlansResponse
            {
                TicketPlans = result.Select(x => new TicketPlanDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Price = x.Price,
                    Days = x.Days,
                    Sessions = x.Sessions,
                    Type = x.Type,
                    Tags = x.Tags?.ToArray() ?? Array.Empty<string>(),
                    FamilyCode = x.FamilyCode,
                    PurchaseKind = x.EligibilityRuleCodes.Contains(
                        "RENEWAL",
                        StringComparer.OrdinalIgnoreCase)
                            ? "RENEWAL"
                            : "STANDARD",
                    Description = x.Description
                }).ToList()
            };
        }
    }
}
