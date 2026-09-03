using gym_system.Api.Contracts.TicketPlans;
using gym_system.Application.TicketPlansUseCase.Queries;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/students/{studentId}/ticket-plans")]
    public sealed class StudentTicketPlansController : ControllerBase
    {
        private readonly GetPurchasableTicketPlansForStudentHandler _handler;

        public StudentTicketPlansController(GetPurchasableTicketPlansForStudentHandler handler)
        {
            _handler = handler;
        }

        [HttpGet("purchasable")]
        public async Task<ActionResult<GetTicketPlansResponse>> GetPurchasableTicketPlansAsync(
            [FromRoute] string studentId,
            CancellationToken ct)
        {
            var result = await _handler.Handle(studentId, ct);

            var response = new GetTicketPlansResponse
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

            return Ok(response);
        }
    }
}
