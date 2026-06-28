using gym_system.Domain.Enums;

namespace gym_system.Application.Common.Authorization
{
    public interface IUserPermissionService
    {
        Task EnsureHasAnyRoleAsync(
            string? userId,
            IReadOnlyCollection<UserRoleCode> allowedRoles,
            CancellationToken ct);
    }
}
