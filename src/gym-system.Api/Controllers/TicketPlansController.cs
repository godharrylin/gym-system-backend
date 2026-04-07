using gym_system.Api.Contracts.TicketPlans;
using gym_system.Application.TicketPlansUseCase.Queries;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/ticket-plans")]

    public class TicketPlansController : ControllerBase
    {
        private readonly ITicketPlanCatalogQuerySerivce _ticketPlanQuerySerivce;
        public TicketPlansController(ITicketPlanCatalogQuerySerivce ticketPlanQuerySerivce)
        {
            _ticketPlanQuerySerivce = ticketPlanQuerySerivce;
        }

        [HttpGet]
        public async Task<ActionResult<GetTicketPlansResponse>> GetActiveTicketPlansAsync(CancellationToken ct)
        {
            IReadOnlyList<TicketPlanResult> result = await _ticketPlanQuerySerivce.GetActiveTicketPlansAsync(ct);

            //  在這裡轉成前端需要的格式
            var response = new GetTicketPlansResponse
            {
                TicketPlans = result.Select(x => new TicketPlanDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Price = x.Price == 0 ? "Free" : $"${x.Price:0}",
                    Days = x.Days,
                    Sessions = x.Sessions.ToString(),
                    Type = x.Type,
                    Tags = x.Tags?.ToArray() ?? Array.Empty<string>(),
                    Description = x.Description
                }).ToList()
            };

            return Ok(response);
        }
    }
}
