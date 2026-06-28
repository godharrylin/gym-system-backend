using gym_system.Api.Contracts.Auth;
using gym_system.Application.AuthUseCase.LoginByPhone;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly LoginByPhoneHandler _loginByPhoneHandler;

        public AuthController(LoginByPhoneHandler loginByPhoneHandler)
        {
            _loginByPhoneHandler = loginByPhoneHandler;
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginByPhoneResponse>> Login(
            [FromBody] LoginByPhoneRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Phone))
            {
                return BadRequest(new { message = "手機號碼必填" });
            }

            try
            {
                var result = await _loginByPhoneHandler.Handle(
                    new LoginByPhoneCommand { Phone = request.Phone },
                    ct);

                return Ok(new LoginByPhoneResponse
                {
                    AccessToken = result.AccessToken,
                    ExpiresAt = result.ExpiresAt,
                    User = new LoginUserResponse
                    {
                        Id = result.User.Id,
                        Name = result.User.Name,
                        Phone = result.User.Phone,
                        Roles = result.User.Roles
                    }
                });
            }
            catch (InvalidOperationException)
            {
                return Unauthorized(new { message = "登入失敗" });
            }
        }
    }
}
