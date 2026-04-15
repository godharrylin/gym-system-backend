using gym_system.Api.Contracts;
using gym_system.Application.MembersUseCase.Commands.RegisterMember;
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
                        ActivationDate = request.TicketPurchase.ActivationDate,
                        PaymentStatus = ParsePaymentState(request.TicketPurchase.PaymentStatus)
                    },
                OperatorId = "ADMIN_PLACEHOLDER"
            };

            var result = await _registerMemberHandler.Handle(command, ct);
            return Ok(result);
        }

        private static RegisterPaymentStatus ParsePaymentState(string status)
        {
            if (status.Equals("PAID", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Paid", StringComparison.OrdinalIgnoreCase))
            {
                return RegisterPaymentStatus.Paid;
            }

            return RegisterPaymentStatus.UnPaid;
        }
    }
}
