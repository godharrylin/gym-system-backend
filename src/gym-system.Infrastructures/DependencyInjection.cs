using gym_system.Application.InstructorsUseCase.Queries;
using gym_system.Application.CoursesUseCase.Queries;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using gym_system.Infrastructures.Connections;
using gym_system.Infrastructures.Dapper;
using gym_system.Infrastructures.Queries.Instructors;
using gym_system.Infrastructures.Queries.Courses;
using gym_system.Infrastructures.Queries.TicketPlans;
using Microsoft.Extensions.DependencyInjection;
using gym_system.Application.ScheduleRulesUseCase.Queries;
using gym_system.Infrastructures.Queries.ScheduleRules;

namespace gym_system.Infrastructures
{
    public static class DependencyInjection
    {
        //  this IServiceCollection services 是擴充方法
        //  Mock　Data
        public static IServiceCollection AddInfrastructureInMemory(this IServiceCollection services)
        {
            services.AddCommonInfrastructure();

            services.AddSingleton<InMemoryStore>();
            services.AddScoped<IUserRepository, InMemoryUserRepository>();
            services.AddScoped<IUserRoleRepository, InMemoryUserRoleRepository>();
            services.AddScoped<IStudentProfileRepository, InMemoryStudentProfileRepository>();
            services.AddScoped<ITicketPlanRepository, InMemoryTicketPlanRepository>();
            services.AddScoped<IOrderRepository, InMemoryOrderRepository>();
            services.AddScoped<ITicketPassRepository, InMemoryTicketPassRepository>();
            
            services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
            services.AddScoped<ITicketPlanCatalogQueryService, DapperTicketPlanCatalogQueryService>();

            return services;
        }

        //  MS SQL
        public static IServiceCollection AddInfrastructureSql(this IServiceCollection services)
        {
            services.AddCommonInfrastructure();
            services.AddScoped<IUnitOfWork, SqlUnitOfWork>();

            services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
            services.AddScoped<ITicketPlanCatalogQueryService, DapperTicketPlanCatalogQueryService>();
            services.AddScoped<IStudentProfileRepository, SqlStudentProfileRepository>();

            return services;
        }

        //  Common Need
        private static IServiceCollection AddCommonInfrastructure(this IServiceCollection services)
        {
            services.AddScoped<IUnitOfWork, NoopUnitOfWork>();
            services.AddScoped<IClock, TaipeiClock>();
            services.AddScoped<IUserRepository, SqlUserRepository>();
            services.AddScoped<IUserRoleRepository, SqlUserRoleRepository>();
            services.AddScoped<ICourseRepository, SqlCourseRepository>();
            services.AddScoped<IInstructorQueryService, DapperGetInstructorsQueryService>();
            services.AddScoped<ISqlSession, SqlSession>();
            services.AddScoped<IScheduleRuleRepository, SqlScheduleRuleRepository>();
            services.AddScoped<IScheduleSessionRepository, SqlScheduleSessionRepository>();
            services.AddScoped<IScheduleSessionLogRepository, SqlScheduleSessionLogRepository>();
            DapperConfig.Register();
            services.AddScoped<ICourseCatalogQueryService, DapperCourseCatalogQueryService>();
            services.AddScoped<IScheduleRulesQueryService, DapperGetScheduleRulesQueryService>();
            return services;
        }
    }

    internal sealed class InMemoryStore
    {
        public List<StudentProfile> Profiles { get; } = [];
        public List<User> Users { get; } = [];
        public List<UserRole> UserRoles { get; } = [];
        public List<Order> Orders { get; } = [];
        public List<TicketPass> Passes { get; } = [];
        public List<TicketPlanKind> TicketPlans { get; } =
        [
            new TicketPlanKind
            {
                Id = "T_001",
                Name = "Single",
                Type = TicketPlanType.Pack,
                Price = 250,
                DefaultCredit = 1,
                DefaultExpireDays = 1,
                IsActive = true
            },
            new TicketPlanKind
            {
                Id = "T_002",
                Name = "Pack 10",
                Type = TicketPlanType.Pack,
                Price = 2300,
                DefaultCredit = 10,
                DefaultExpireDays = 90,
                IsActive = true
            },
            new TicketPlanKind
            {
                Id = "T_003",
                Name = "Monthly",
                Type = TicketPlanType.MPass,
                Price = 1960,
                DefaultCredit = 999,
                DefaultExpireDays = 30,
                IsActive = true
            }
        ];
    }

    internal sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryUserRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct)
        {
            IReadOnlyList<string> result = _store.Users
                .Where(user => phones.Contains(user.Phone, StringComparer.Ordinal))
                .Select(user => user.Phone)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<string> AddAsync(User user, CancellationToken ct)
        {
            var userId = $"U{_store.Users.Count + 1:0000000000}";
            _store.Users.Add(User.Rehydrate(
                userId,
                user.Name,
                user.Phone,
                user.Password,
                user.IsActive));
            return Task.FromResult(userId);
        }

        public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct)
        {
            return Task.FromResult(_store.Users.FirstOrDefault(user => user.Id == userId));
        }

        public Task<User?> FindUserByPhoneAsync(string phone, CancellationToken ct)
        {
            return Task.FromResult(_store.Users.FirstOrDefault(user => user.Phone == phone));
        }

        public Task<bool> ExistsPhoneForOtherUserAsync(string userId, string phone, CancellationToken ct)
        {
            return Task.FromResult(_store.Users.Any(user => user.Id != userId && user.Phone == phone));
        }

        public Task<bool> UpdateBasicProfileAsync(string userId, string name, string phone, CancellationToken ct)
        {
            var index = _store.Users.FindIndex(user => user.Id == userId);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            var current = _store.Users[index];
            _store.Users[index] = User.Rehydrate(
                current.Id,
                string.IsNullOrWhiteSpace(name) ? current.Name : name,
                string.IsNullOrWhiteSpace(phone) ? current.Phone : phone,
                current.Password,
                current.IsActive);
            return Task.FromResult(true);
        }
    }

    internal sealed class InMemoryUserRoleRepository : IUserRoleRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryUserRoleRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
        {
            return Task.FromResult(_store.UserRoles.FirstOrDefault(
                role => role.UserId == userId && role.RoleCode == roleType));
        }

        public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct)
        {
            IReadOnlyList<UserRole> result = _store.UserRoles
                .Where(role => role.UserId == userId && role.IsActive)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct)
        {
            var exists = _store.UserRoles.Any(role =>
                role.UserId == userRole.UserId && role.RoleCode == userRole.RoleCode);
            if (!exists)
            {
                _store.UserRoles.Add(userRole);
            }

            return Task.FromResult(true);
        }

        public Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
        {
            return SetRoleActiveAsync(userId, roleType, true, ct);
        }

        public Task<bool> SetRoleActiveAsync(
            string userId,
            UserRoleCode roleType,
            bool isActive,
            CancellationToken ct)
        {
            var index = _store.UserRoles.FindIndex(role =>
                role.UserId == userId && role.RoleCode == roleType);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            var current = _store.UserRoles[index];
            _store.UserRoles[index] = UserRole.Assign(
                current.UserId,
                current.RoleCode,
                current.AssignedAt,
                isActive);
            return Task.FromResult(true);
        }
    }

    internal sealed class InMemoryStudentProfileRepository : IStudentProfileRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryStudentProfileRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task AddAsync(StudentProfile profile, CancellationToken ct)
        {
            _store.Profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct)
        {
            _store.Profiles.AddRange(profiles);
            return Task.CompletedTask;
        }

        public Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct)
        {
            var profile = _store.Profiles.FirstOrDefault(x => x.UserId == userId);
            return Task.FromResult(profile);
        }

        public Task<bool> UpdateLastVisitAsync(string userId, DateTime lastVisitAt, CancellationToken ct)
        {
            var profile = _store.Profiles.FirstOrDefault(x => x.UserId == userId);
            if (profile is null)
            {
                return Task.FromResult(false);
            }

            profile.RecordVisit(lastVisitAt);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct)
        {
            var profile = _store.Profiles.FirstOrDefault(x => x.UserId == userId);
            if (profile is null)
            {
                return Task.FromResult(false);
            }

            profile.UpdateCurrentTicket(snapshot);
            return Task.FromResult(true);
        }
    }

    internal sealed class InMemoryTicketPlanRepository : ITicketPlanRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryTicketPlanRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct)
        {
            var plan = _store.TicketPlans.FirstOrDefault(x => x.Id == ticketPlanKindId && x.IsActive);
            return Task.FromResult(plan);
        }
    }

    internal sealed class InMemoryOrderRepository : IOrderRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryOrderRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task AddAsync(Order order, CancellationToken ct)
        {
            _store.Orders.Add(order);
            return Task.CompletedTask;
        }
    }

    internal sealed class InMemoryTicketPassRepository : ITicketPassRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryTicketPassRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task AddRangeAsync(IReadOnlyList<TicketPass> passes, CancellationToken ct)
        {
            _store.Passes.AddRange(passes);
            return Task.CompletedTask;
        }
    }

    internal sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task BeginAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    internal sealed class TaipeiClock : IClock
    {
        //  台北時間
        private static readonly TimeZoneInfo TaipeiTimeZone =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

        public DateTime Now()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TaipeiTimeZone);
        }

        public DateOnly Today() => DateOnly.FromDateTime(Now());
    }
}
