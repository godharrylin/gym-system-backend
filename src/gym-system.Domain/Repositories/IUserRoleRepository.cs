using gym_system.Domain.Enums;
using gym_system.Domain.Entities.Users;

namespace gym_system.Domain.Repositories
{
    public interface IUserRoleRepository
    {
        /// <summary>
        /// 檢查已存在的使用者是否有指定的角色。如果有，回傳角色資訊
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="roleType"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct);
        public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct);
        public Task<bool> ReactiveRole(string userId, UserRoleCode roleType, CancellationToken ct);
        public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct);
    }
}
