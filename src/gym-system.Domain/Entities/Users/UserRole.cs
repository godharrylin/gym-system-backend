using gym_system.Domain.Enums;

namespace gym_system.Domain.Entities.Users
{
    //  角色關聯，支援一人多角色
    public sealed class UserRole
    {
        private UserRole(string userId, UserRoleCode roleCode, DateTime assignedAt, bool isActive)
        {
            UserId = userId;
            RoleCode = roleCode;
            AssignedAt = assignedAt;
            IsActive = isActive;
        }

        public string UserId { get; }
        public UserRoleCode RoleCode { get; }
        public DateTime AssignedAt { get; }
        public bool IsActive { get; private set; }

        public static UserRole Assign(string userId, UserRoleCode roleCode, DateTime now, bool isActiveRole)
        {
            if (string.IsNullOrWhiteSpace(userId)) throw new InvalidOperationException("UserId 必填");
            return new UserRole(userId, roleCode, now, isActiveRole);
        }

        public void Deactivate()
        {
            IsActive = false;
        }
    }
}
