using gym_system.Application.InstructorsUseCase.Queries;
using gym_system.Application.CoursesUseCase.Queries;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Application.TicketsUseCase.Queries;
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
using gym_system.Infrastructures.Queries.TicketPasses;
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
            services.AddScoped<IStudentTicketPassQueryService, DapperStudentTicketPassQueryService>();

            return services;
        }

        //  MS SQL
        public static IServiceCollection AddInfrastructureSql(this IServiceCollection services)
        {
            services.AddCommonInfrastructure();
            services.AddScoped<IUnitOfWork, SqlUnitOfWork>();

            services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
            services.AddScoped<ITicketPlanCatalogQueryService, DapperTicketPlanCatalogQueryService>();
            services.AddScoped<IStudentTicketPassQueryService, DapperStudentTicketPassQueryService>();
            services.AddScoped<IStudentProfileRepository, SqlStudentProfileRepository>();
            services.AddScoped<ITicketPlanRepository, SqlTicketPlanRepository>();
            services.AddScoped<IOrderRepository, SqlOrderRepository>();
            services.AddScoped<ITicketPassRepository, SqlTicketPassRepository>();

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
            services.AddScoped<IStudentTicketPurchaseHistoryQueryService, DapperStudentTicketPurchaseHistoryQueryService>();
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
                Id = "SINGLE",
                Name = "Single",
                FamilyCode = "SINGLE",
                Type = TicketPlanType.Pack,
                Price = 250,
                DefaultCredit = 1,
                DefaultExpireDays = null,
                IsActive = true
            },
            new TicketPlanKind
            {
                Id = "T_002",
                Name = "Pack 10",
                FamilyCode = "PACK_10",
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
                FamilyCode = "MONTHLY",
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
        public Task<bool> ClearCurrentTicketAsync(string userId, DateTime updatedAt, CancellationToken ct)
        {
            var profile = _store.Profiles.FirstOrDefault(x => x.UserId == userId);
            if (profile is null)
            {
                return Task.FromResult(false);
            }

            profile.ClearCurrentTicket();
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

        public Task<OrderPersistenceResult> AddAsync(Order order, CancellationToken ct)
        {
            _store.Orders.Add(order);
            var orderSn = _store.Orders.Count;
            return Task.FromResult(new OrderPersistenceResult
            {
                OrderSn = orderSn,
                OrderId = order.Id,
                Items = order.Items.Select((item, index) => new OrderItemPersistenceResult
                {
                    ClientItemId = item.Id,
                    OrderItemSn = index + 1,
                    OrderItemId = item.Id
                }).ToList()
            });
        }

        public Task<UnpaidTicketOrder?> FindUnpaidTicketOrderAsync(
            string orderId,
            bool acquireLock,
            CancellationToken ct)
        {
            var orderIndex = _store.Orders.FindIndex(x =>
                x.Id.Equals(orderId, StringComparison.OrdinalIgnoreCase));
            if (orderIndex < 0)
            {
                return Task.FromResult<UnpaidTicketOrder?>(null);
            }

            var order = _store.Orders[orderIndex];
            var items = order.Items
                .Where(x => x.Type == OrderItemType.Ticket
                    && x.PaymentState == OrderItemPaymentState.UnPaid)
                .ToList();
            if (order.PaymentState != OrderOverallPaymentState.UnPaid || items.Count == 0)
            {
                return Task.FromResult<UnpaidTicketOrder?>(null);
            }

            if (items.Count != 1)
            {
                throw new InvalidOperationException("目前只支援單一票券明細的未付款訂單");
            }

            var item = items[0];
            return Task.FromResult<UnpaidTicketOrder?>(new UnpaidTicketOrder
            {
                OrderSn = orderIndex + 1,
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                OrderItemSn = order.Items.ToList().IndexOf(item) + 1,
                OrderItemId = item.Id,
                TicketPlanKindCode = item.RefId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalAmount = item.TotalAmount,
                ActualAmount = item.ActualAmount
            });
        }

        public Task MarkPaidAsync(
            int orderSn,
            int orderItemSn,
            DateTime paidAt,
            string paymentMethod,
            string operatorId,
            CancellationToken ct)
        {
            if (orderSn <= 0 || orderSn > _store.Orders.Count)
            {
                throw new InvalidOperationException("訂單不存在");
            }

            var order = _store.Orders[orderSn - 1];
            if (orderItemSn <= 0 || orderItemSn > order.Items.Count)
            {
                throw new InvalidOperationException("訂單明細不存在");
            }

            order.Items[orderItemSn - 1].MarkPaid(paidAt);
            order.MarkPaid();
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

        public Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(
            IReadOnlyList<TicketPass> passes,
            OrderPersistenceResult orderPersistence,
            CancellationToken ct)
        {
            _store.Passes.AddRange(passes);
            IReadOnlyList<TicketPassPersistenceResult> result = passes.Select((pass, index) => new TicketPassPersistenceResult
            {
                ClientPassId = pass.Id,
                OwnerId = pass.OwnerId,
                PassSn = index + 1,
                PassId = pass.Id
            }).ToList();
            return Task.FromResult(result);
        }

        public Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(
            string ownerId,
            string familyCode,
            bool acquireLock,
            CancellationToken ct)
        {
            var candidates = _store.Passes
                .Select((pass, index) => new { Pass = pass, PassSn = index + 1 })
                .Where(x => x.Pass.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)
                    && x.Pass.PaymentState == PaymentState.Paid
                    && x.Pass.ValidStatus is TicketValidStatus.Active
                        or TicketValidStatus.Expire
                        or TicketValidStatus.Depleted
                    && x.Pass.ValidStartDate is not null
                    && !string.IsNullOrWhiteSpace(x.Pass.Plan.FamilyCode)
                    && x.Pass.Plan.FamilyCode.Equals(familyCode, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Pass.ValidStartDate)
                .ThenByDescending(x => x.PassSn)
                .ToList();
            var candidate = candidates.FirstOrDefault();
            if (candidate is null)
            {
                return Task.FromResult<RenewalSourcePass?>(null);
            }

            var children = _store.Passes
                .Where(x => x.RenewedFromPassSn == candidate.PassSn)
                .ToList();
            var cancelledChildren = children
                .Where(x => x.ValidStatus == TicketValidStatus.Cancelled)
                .ToList();
            return Task.FromResult<RenewalSourcePass?>(new RenewalSourcePass
            {
                PassSn = candidate.PassSn,
                FamilyCode = candidate.Pass.Plan.FamilyCode!,
                ValidStatus = candidate.Pass.ValidStatus,
                ValidStartDate = candidate.Pass.ValidStartDate,
                ValidEndDate = candidate.Pass.ValidEndDate,
                EndedAt = candidate.Pass.EndedAt,
                EndReason = candidate.Pass.EndReason,
                HasNonCancelledRenewal = children.Any(x => x.ValidStatus != TicketValidStatus.Cancelled),
                HasCancelledRenewal = cancelledChildren.Count > 0,
                LastCancelledRenewalAt = cancelledChildren.Max(x => x.EndedAt)
            });
        }

        public Task<bool> HasQueuedPassAsync(string ownerId, CancellationToken ct)
        {
            return Task.FromResult(_store.Passes.Any(x =>
                x.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)
                && x.PaymentState == PaymentState.Paid
                && x.ValidStatus == TicketValidStatus.UnActive
                && x.PaidAt is not null
                && !IsSingle(x)));
        }

        public Task LockOwnerAsync(string ownerId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(
            string passId,
            DateTime cancelledAt,
            string operatorId,
            CancellationToken ct)
        {
            var item = _store.Passes
                .Select((pass, index) => new { Pass = pass, PassSn = index + 1 })
                .FirstOrDefault(x => x.Pass.Id.Equals(passId, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                return Task.FromResult<CancelledTicketPassResult?>(null);
            }

            item.Pass.Cancel(cancelledAt);
            return Task.FromResult<CancelledTicketPassResult?>(new CancelledTicketPassResult
            {
                PassId = item.Pass.Id,
                OwnerId = item.Pass.OwnerId,
                SourcePassSn = item.Pass.RenewedFromPassSn!.Value
            });
        }
        public Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(
            string ownerId,
            DateOnly today,
            DateTime updatedAt,
            string operatorId,
            CancellationToken ct)
        {
            var ownerPasses = _store.Passes
                .Select((pass, index) => new InMemoryPassItem(pass, index + 1))
                .Where(x => x.Pass.OwnerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase)
                    && x.Pass.PaymentState == PaymentState.Paid)
                .ToList();
            var active = ownerPasses
                .Where(x => x.Pass.ValidStatus == TicketValidStatus.Active)
                .ToList();
            if (active.Count > 1)
            {
                throw new InvalidOperationException($"學生 {ownerId} 同時存在多張 Active 票券");
            }

            int? justEndedPassSn = null;
            if (active.Count == 1)
            {
                var current = active[0];
                current.Pass.RefreshStatus(today);
                if (current.Pass.ValidStatus != TicketValidStatus.Active)
                {
                    justEndedPassSn = current.PassSn;
                    active.Clear();
                }
            }

            if (active.Count == 1 && IsSingle(active[0].Pass))
            {
                var nextNonSingle = FindNextActivatable(
                    ownerPasses,
                    today,
                    justEndedPassSn,
                    includeSingle: false);
                if (nextNonSingle is not null)
                {
                    active[0].Pass.YieldSingleToQueue();
                    nextNonSingle.Pass.Activate(nextNonSingle.ActivationDate);
                    nextNonSingle.Pass.RefreshStatus(today);
                    active = ownerPasses
                        .Where(x => x.Pass.ValidStatus == TicketValidStatus.Active)
                        .ToList();
                }
            }

            while (active.Count == 0)
            {
                var next = FindNextActivatable(
                    ownerPasses,
                    today,
                    justEndedPassSn,
                    includeSingle: true);
                if (next is not null)
                {
                    next.Pass.Activate(next.ActivationDate);
                    next.Pass.RefreshStatus(today);
                    if (next.Pass.ValidStatus != TicketValidStatus.Active)
                    {
                        continue;
                    }
                }

                active = ownerPasses
                    .Where(x => x.Pass.ValidStatus == TicketValidStatus.Active)
                    .ToList();
                break;
            }

            return Task.FromResult(active.SingleOrDefault()?.Pass.ToSnapshot());
        }

        private ActivatableInMemoryPass? FindNextActivatable(
            IReadOnlyList<InMemoryPassItem> ownerPasses,
            DateOnly today,
            int? justEndedPassSn,
            bool includeSingle)
        {
            foreach (var item in ownerPasses
                .Where(x => x.Pass.ValidStatus == TicketValidStatus.UnActive)
                .Where(x => x.Pass.PaidAt is not null)
                .Where(x => includeSingle || !IsSingle(x.Pass))
                .OrderBy(x => IsSingle(x.Pass) ? 1 : 0)
                .ThenBy(x =>
                {
                    if (justEndedPassSn is not null && x.Pass.RenewedFromPassSn == justEndedPassSn)
                    {
                        return 0;
                    }

                    return x.Pass.RenewedFromPassSn is null ? 2 : 1;
                })
                .ThenBy(x => x.Pass.PaidAt ?? DateTime.MinValue)
                .ThenBy(x => x.PassSn))
            {
                var activationDate = today;
                if (item.Pass.RenewedFromPassSn is int sourcePassSn)
                {
                    var sourceIndex = sourcePassSn - 1;
                    if (sourceIndex < 0 || sourceIndex >= _store.Passes.Count)
                    {
                        continue;
                    }

                    var source = _store.Passes[sourceIndex];
                    if (source.ValidStatus is not TicketValidStatus.Expire
                        and not TicketValidStatus.Depleted)
                    {
                        continue;
                    }

                    var sourceEndDate = source.EndReason == TicketEndReason.Depleted
                        ? source.EndedAt is null
                            ? throw new InvalidOperationException("續約來源缺少實際用完時間")
                            : DateOnly.FromDateTime(source.EndedAt.Value)
                        : source.ValidEndDate
                            ?? throw new InvalidOperationException("續約來源缺少到期日");
                    var paidDate = DateOnly.FromDateTime(
                        item.Pass.PaidAt
                            ?? throw new InvalidOperationException("續約票缺少付款時間"));
                    activationDate = TicketActivationSchedule.GetRenewalStartDate(
                        sourceEndDate,
                        paidDate);
                    if (activationDate > today)
                    {
                        continue;
                    }
                }

                // Expired renewal candidates must not make an active SINGLE yield.
                // Like SQL, end the candidate and reconsider its successor first.
                if (!IsSingle(item.Pass))
                {
                    var days = item.Pass.Plan.DefaultExpireDays
                        ?? throw new InvalidOperationException("非單次票方案缺少有效天數");
                    if (TicketActivationSchedule.GetEndDate(activationDate, days) < today)
                    {
                        item.Pass.Activate(activationDate);
                        item.Pass.RefreshStatus(today);
                        return FindNextActivatable(ownerPasses, today, item.PassSn, includeSingle);
                    }
                }

                return new ActivatableInMemoryPass(item.Pass, activationDate);
            }

            return null;
        }

        private static bool IsSingle(TicketPass pass) =>
            pass.Plan.Id.Equals("SINGLE", StringComparison.OrdinalIgnoreCase);

        private sealed record ActivatableInMemoryPass(TicketPass Pass, DateOnly ActivationDate);

        private sealed record InMemoryPassItem(TicketPass Pass, int PassSn);
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
