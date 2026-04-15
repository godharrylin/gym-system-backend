using gym_system.Domain.Enums;
using gym_system.Domain.Entities.Users;

namespace gym_system.Domain.Repositories
{
    public interface IUserRoleRepository
    {
        public Task<UserRole> CheckRoleAsync(string userId, UserRoleCode roleType);
        public Task<bool> AddRoleAsync(UserRole userRole, UserRoleCode roleType, CancellationToken ct);
        public Task<bool> ReactiveRole(string userId, UserRoleCode roleType, CancellationToken ct);
    }
}
