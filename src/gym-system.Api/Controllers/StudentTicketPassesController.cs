using System.Globalization;
using gym_system.Api.Contracts.TicketPasses;
using gym_system.Application.TicketsUseCase.Queries;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/students/{studentId}/ticket-passes")]
    public sealed class StudentTicketPassesController : ControllerBase
    {
        private readonly GetStudentTicketPassesHandler _handler;

        public StudentTicketPassesController(GetStudentTicketPassesHandler handler)
        {
            _handler = handler;
        }

        [HttpGet]
        public async Task<ActionResult<GetStudentTicketPassesResponse>> GetAsync(
            [FromRoute] string studentId,
            CancellationToken ct)
        {
            try
            {
                var result = await _handler.Handle(studentId, ct);
                return Ok(new GetStudentTicketPassesResponse
                {
                    TicketPasses = result.Select(x => new StudentTicketPassDto
                    {
                        PassId = x.PassId,
                        PlanCode = x.PlanCode,
                        PlanName = x.PlanName,
                        PlanType = x.PlanType,
                        ValidStatus = x.ValidStatus,
                        PaymentStatus = x.PaymentStatus,
                        UnitPrice = x.UnitPrice,
                        PaidAt = x.PaidAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        PaidAtTimestamp = FormatTaipeiTimestamp(x.PaidAt),
                        ValidStartDate = x.ValidStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ValidEndDate = x.ValidEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        CreditsTotal = x.CreditsTotal,
                        CreditsRemaining = x.CreditsRemaining,
                        CanCancel = x.CanCancel
                    }).ToList()
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { code = "STUDENT_NOT_FOUND", message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { code = "TICKET_STATE_CONFLICT", message = ex.Message });
            }
        }

        private static string FormatTaipeiTimestamp(DateTime value)
        {
            var localTime = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            return new DateTimeOffset(localTime, TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }
    }
}
