using gym_system.Domain.Enums;
using gym_system.Domain.Entities;
using gym_system.Domain.Entities.Users;

namespace gym_system.Domain.Repositories
{
    public interface IUserRoleRepository
    {
        public Task<bool> HasRoleAsync(string userId, UserRoleCode roleType);
        public Task<bool> AddRoleAsync(UserRole userRole);
    }
}
