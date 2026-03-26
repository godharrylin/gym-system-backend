using gym_system.Application.MembersUseCase.Commands.RegisterMember;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    public class MemberController : ControllerBase
    {
        private readonly RegisterMemberHandler _registerMemberHandler;

        public MemberController(RegisterMemberHandler registerMemberHandler)
        {
            _registerMemberHandler = registerMemberHandler;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterMemberCommand command)
        {
            await _registerMemberHandler.Handle(command);
            return Ok("註冊檢查通過");
        }

    }
}
