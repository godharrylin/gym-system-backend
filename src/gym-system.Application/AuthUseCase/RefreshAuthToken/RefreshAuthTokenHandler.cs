using gym_system.Application.AuthUseCase.Tokens;
using gym_system.Domain.Repositories;

namespace gym_system.Application.AuthUseCase.RefreshAuthToken
{
    public sealed class RefreshAuthTokenHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IAuthTokenGenerator _authTokenGenerator;

        public RefreshAuthTokenHandler(
            IUserRepository userRepository,
            IUserRoleRepository userRoleRepository,
            IAuthTokenGenerator authTokenGenerator)
        {
            _userRepository = userRepository;
            _userRoleRepository = userRoleRepository;
            _authTokenGenerator = authTokenGenerator;
        }

        public async Task<RefreshAuthTokenResult> Handle(RefreshAuthTokenCommand command, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(command.UserId))
            {
                throw new InvalidOperationException("使用者識別必填");
            }

            var user = await _userRepository.FindUserByIdAsync(command.UserId.Trim(), ct)
                ?? throw new InvalidOperationException("使用者不存在");

            if (!user.IsActive)
            {
                throw new InvalidOperationException("使用者已停用");
            }

            var roles = (await _userRoleRepository.GetActiveRolesAsync(user.Id, ct))
                .Select(role => role.RoleCode.ToString())
                .ToList();

            var tokens = _authTokenGenerator.Generate(user, roles);

            return new RefreshAuthTokenResult
            {
                AccessToken = tokens.AccessToken,
                AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
                RefreshToken = tokens.RefreshToken,
                RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
                User = new RefreshUserResult
                {
                    Id = user.Id,
                    Name = user.Name,
                    Phone = user.Phone,
                    Roles = roles
                }
            };
        }
    }
}
