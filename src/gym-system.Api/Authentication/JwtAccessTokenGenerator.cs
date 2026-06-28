using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using gym_system.Application.AuthUseCase.LoginByPhone;
using gym_system.Domain.Entities.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace gym_system.Api.Authentication
{
    public sealed class JwtAccessTokenGenerator : IAccessTokenGenerator
    {
        private readonly JwtSettings _settings;

        public JwtAccessTokenGenerator(IOptions<JwtSettings> options)
        {
            _settings = options.Value;
        }

        public AccessTokenResult Generate(User user, IReadOnlyList<string> roles)
        {
            var now = DateTime.UtcNow;
            var expiresAt = now.AddMinutes(_settings.ExpiresMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Name, user.Name),
                new("phone", user.Phone),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64)
            };

            claims.AddRange(roles.Select(role => new Claim("role", role)));

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: now,
                expires: expiresAt,
                signingCredentials: credentials);

            return new AccessTokenResult
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAt = expiresAt
            };
        }
    }
}
