using gym_system.Application.TicketsUseCase.Commands.CancelQueuedRenewal;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/ticket-passes")]
    public sealed class TicketPassesController : ControllerBase
    {
        private readonly CancelQueuedRenewalHandler _cancelHandler;

        public TicketPassesController(CancelQueuedRenewalHandler cancelHandler)
        {
            _cancelHandler = cancelHandler;
        }

        [HttpPost("{passId}/cancel")]
        public async Task<IActionResult> CancelAsync(
            [FromRoute] string passId,
            CancellationToken ct)
        {
            try
            {
                await _cancelHandler.Handle(passId, "ADMIN_PLACEHOLDER", ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { code = "TICKET_PASS_NOT_FOUND", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "TICKET_PASS_CANNOT_CANCEL", message = ex.Message });
            }
        }
    }
}
