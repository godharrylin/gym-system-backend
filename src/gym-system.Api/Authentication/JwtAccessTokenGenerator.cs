using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using gym_system.Application.AuthUseCase.Tokens;
using gym_system.Domain.Entities.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace gym_system.Api.Authentication
{
    public sealed class JwtAuthTokenGenerator : IAuthTokenGenerator
    {
        private readonly JwtSettings _settings;

        public JwtAuthTokenGenerator(IOptions<JwtSettings> options)
        {
            _settings = options.Value;
        }

        public AuthTokenResult Generate(User user, IReadOnlyList<string> roles)
        {
            var now = DateTime.UtcNow;
            var accessTokenExpiresAt = now.AddMinutes(_settings.ExpiresMinutes);
            var refreshTokenExpiresAt = now.AddDays(_settings.RefreshExpiresDays);

            var accessClaims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Name, user.Name),
                new("phone", user.Phone),
                new("token_type", "access"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64)
            };

            accessClaims.AddRange(roles.Select(role => new Claim("role", role)));

            var refreshClaims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new("token_type", "refresh"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64)
            };

            return new AuthTokenResult
            {
                AccessToken = WriteToken(accessClaims, now, accessTokenExpiresAt),
                AccessTokenExpiresAt = accessTokenExpiresAt,
                RefreshToken = WriteToken(refreshClaims, now, refreshTokenExpiresAt),
                RefreshTokenExpiresAt = refreshTokenExpiresAt
            };
        }

        private string WriteToken(IReadOnlyList<Claim> claims, DateTime notBefore, DateTime expiresAt)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: notBefore,
                expires: expiresAt,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
