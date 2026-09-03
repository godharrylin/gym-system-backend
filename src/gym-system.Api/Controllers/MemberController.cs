using gym_system.Api.Contracts;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/users")]
    public class MemberController : ControllerBase
    {
        private readonly RegisterMemberHandler _registerMemberHandler;

        public MemberController(RegisterMemberHandler registerMemberHandler)
        {
            _registerMemberHandler = registerMemberHandler;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterMembersRequest request, CancellationToken ct)
        {
            try
            {
                var command = new RegisterMembersCommand
                {
                    Members = request.Members.Select(x => new MemberRegisterInput
                    {
                        Name = x.Name,
                        Phone = x.Phone
                    }).ToList(),
                    TicketPurchase = request.TicketPurchase is null
                        ? null
                        : new TicketPurchaseInput
                        {
                            TicketPlanKindId = request.TicketPurchase.TicketPlanKindId,
                            Quantity = request.TicketPurchase.Quantity,
                            PaymentStatus = ParsePaymentState(request.TicketPurchase.PaymentStatus)
                        },
                    OperatorId = "ADMIN_PLACEHOLDER"
                };

                var result = await _registerMemberHandler.Handle(command, ct);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { code = "TICKET_PLAN_NOT_AVAILABLE", message = ex.Message });
            }
            catch (TicketPurchaseRejectedException ex)
            {
                return Conflict(new { code = ex.Code, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { code = "MEMBER_REGISTRATION_REJECTED", message = ex.Message });
            }
        }

        private static PaymentState ParsePaymentState(string status)
        {
            if (status.Equals("PAID", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Paid", StringComparison.OrdinalIgnoreCase))
            {
                return PaymentState.Paid;
            }

            if (status.Equals("UNPAID", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("UnPaid", StringComparison.OrdinalIgnoreCase))
            {
                return PaymentState.UnPaid;
            }

            throw new InvalidOperationException("付款狀態不支援");
        }
    }
}
