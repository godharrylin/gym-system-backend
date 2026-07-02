using gym_system.Api.Authentication;
using gym_system.Api.Contracts.Auth;
using gym_system.Application.AuthUseCase.LoginByPhone;
using gym_system.Application.AuthUseCase.RefreshAuthToken;
using Microsoft.AspNetCore.Mvc;

namespace gym_system.Api.Controllers
{
    [ApiController]
    [Route("api/v1/auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly LoginByPhoneHandler _loginByPhoneHandler;
        private readonly RefreshAuthTokenHandler _refreshAuthTokenHandler;
        private readonly IRefreshTokenValidator _refreshTokenValidator;

        public AuthController(
            LoginByPhoneHandler loginByPhoneHandler,
            RefreshAuthTokenHandler refreshAuthTokenHandler,
            IRefreshTokenValidator refreshTokenValidator)
        {
            _loginByPhoneHandler = loginByPhoneHandler;
            _refreshAuthTokenHandler = refreshAuthTokenHandler;
            _refreshTokenValidator = refreshTokenValidator;
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
                    AccessTokenExpiresAt = result.AccessTokenExpiresAt,
                    RefreshToken = result.RefreshToken,
                    RefreshTokenExpiresAt = result.RefreshTokenExpiresAt,
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

        [HttpPost("refresh")]
        public async Task<ActionResult<RefreshAuthTokenResponse>> Refresh(
            [FromBody] RefreshAuthTokenRequest request,
            CancellationToken ct)
        {
            var validationResult = _refreshTokenValidator.Validate(request.RefreshToken);
            if (validationResult is null)
            {
                return Unauthorized(new { message = "Refresh token 無效" });
            }

            try
            {
                var result = await _refreshAuthTokenHandler.Handle(
                    new RefreshAuthTokenCommand { UserId = validationResult.UserId },
                    ct);

                return Ok(new RefreshAuthTokenResponse
                {
                    AccessToken = result.AccessToken,
                    AccessTokenExpiresAt = result.AccessTokenExpiresAt,
                    RefreshToken = result.RefreshToken,
                    RefreshTokenExpiresAt = result.RefreshTokenExpiresAt,
                    User = new RefreshUserResponse
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
                return Unauthorized(new { message = "Refresh token 無效" });
            }
        }
    }
}
