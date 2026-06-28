using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;

namespace gym_system.Application.Common.Authorization
{
    public sealed class UserPermissionService : IUserPermissionService
    {
        private readonly IUserRoleRepository _userRoleRepository;

        public UserPermissionService(IUserRoleRepository userRoleRepository)
        {
            _userRoleRepository = userRoleRepository;
        }

        public async Task EnsureHasAnyRoleAsync(
            string? userId,
            IReadOnlyCollection<UserRoleCode> allowedRoles,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ForbiddenException("缺少使用者身分");
            }

            if (allowedRoles.Count == 0)
            {
                throw new InvalidOperationException("至少需要一個允許角色");
            }

            var normalizedUserId = userId.Trim();
            foreach (var allowedRole in allowedRoles)
            {
                var role = await _userRoleRepository.GetUserRoleAsync(
                    normalizedUserId,
                    allowedRole,
                    ct);

                if (role is not null && role.IsActive)
                {
                    return;
                }
            }

            throw new ForbiddenException("沒有操作權限");
        }
    }
}
