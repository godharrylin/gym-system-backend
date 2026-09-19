using System.Diagnostics;
using gym_system.Api.Contracts;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
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
        [Authorize(Roles = "Admin")]
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
                return NotFound(CreateError("TICKET_PLAN_NOT_AVAILABLE", ex.Message));
            }
            catch (TicketPurchaseRejectedException ex)
            {
                return Conflict(CreateError(ex.Code, ex.Message));
            }
            catch (MemberRegistrationRejectedException ex)
            {
                var error = CreateError(ex.Code, ex.Message);
                return ex.Code == "MEMBER_PHONE_ALREADY_REGISTERED"
                    ? Conflict(error)
                    : BadRequest(error);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(CreateError("MEMBER_REGISTRATION_REJECTED", ex.Message));
            }
        }

        private ApiErrorResponse CreateError(string code, string message)
        {
            return new ApiErrorResponse
            {
                Code = code,
                Message = message,
                TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };
        }

        private static PaymentState ParsePaymentState(string status)
        {
            if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                return PaymentState.Paid;
            }

            if (string.Equals(status, "UNPAID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "UnPaid", StringComparison.OrdinalIgnoreCase))
            {
                return PaymentState.UnPaid;
            }

            throw new InvalidOperationException("付款狀態不支援");
        }
    }
}
