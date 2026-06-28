using gym_system.Domain.Repositories;

namespace gym_system.Application.AuthUseCase.LoginByPhone
{
    public sealed class LoginByPhoneHandler
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IAccessTokenGenerator _accessTokenGenerator;

        public LoginByPhoneHandler(
            IUserRepository userRepository,
            IUserRoleRepository userRoleRepository,
            IAccessTokenGenerator accessTokenGenerator)
        {
            _userRepository = userRepository;
            _userRoleRepository = userRoleRepository;
            _accessTokenGenerator = accessTokenGenerator;
        }

        public async Task<LoginByPhoneResult> Handle(LoginByPhoneCommand command, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(command.Phone))
            {
                throw new InvalidOperationException("手機號碼必填");
            }

            var phone = command.Phone.Trim();
            var user = await _userRepository.FindUserByPhoneAsync(phone, ct)
                ?? throw new InvalidOperationException("使用者不存在");

            if (!user.IsActive)
            {
                throw new InvalidOperationException("使用者已停用");
            }

            var roles = (await _userRoleRepository.GetActiveRolesAsync(user.Id, ct))
                .Select(role => role.RoleCode.ToString())
                .ToList();

            var token = _accessTokenGenerator.Generate(user, roles);

            return new LoginByPhoneResult
            {
                AccessToken = token.AccessToken,
                ExpiresAt = token.ExpiresAt,
                User = new LoginUserResult
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
