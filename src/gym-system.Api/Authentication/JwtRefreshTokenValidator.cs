using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace gym_system.Api.Authentication
{
    public sealed class JwtRefreshTokenValidator : IRefreshTokenValidator
    {
        private readonly JwtSettings _settings;

        public JwtRefreshTokenValidator(IOptions<JwtSettings> options)
        {
            _settings = options.Value;
        }

        public RefreshTokenValidationResult? Validate(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            var tokenHandler = new JwtSecurityTokenHandler
            {
                MapInboundClaims = false
            };

            try
            {
                var principal = tokenHandler.ValidateToken(
                    refreshToken.Trim(),
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = _settings.Issuer,
                        ValidateAudience = true,
                        ValidAudience = _settings.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey)),
                        ClockSkew = TimeSpan.FromMinutes(1),
                        NameClaimType = "name",
                        RoleClaimType = "role"
                    },
                    out var validatedToken);

                if (validatedToken is not JwtSecurityToken jwtToken ||
                    !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
                {
                    return null;
                }

                if (principal.FindFirst("token_type")?.Value != "refresh")
                {
                    return null;
                }

                var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return null;
                }

                return new RefreshTokenValidationResult
                {
                    UserId = userId
                };
            }
            catch (SecurityTokenException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
