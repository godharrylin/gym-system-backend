using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace gym_system.Infrastructures
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddSingleton<InMemoryStore>();
            services.AddScoped<IMemberRepository, InMemoryMemberRepository>();
            services.AddScoped<IStudentProfileRepository, InMemoryStudentProfileRepository>();
            services.AddScoped<ITicketPlanRepository, InMemoryTicketPlanRepository>();
            services.AddScoped<IOrderRepository, InMemoryOrderRepository>();
            services.AddScoped<ITicketPassRepository, InMemoryTicketPassRepository>();
            services.AddScoped<IUnitOfWork, NoopUnitOfWork>();
            services.AddScoped<IClock, SystemClock>();

            return services;
        }
    }

    internal sealed class InMemoryStore
    {
        public List<Member> Members { get; } = [];
        public List<StudentProfile> Profiles { get; } = [];
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

    internal sealed class InMemoryMemberRepository : IMemberRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryMemberRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task<bool> AnyPhoneExistsAsync(IReadOnlyList<string> phones, CancellationToken ct)
        {
            var exists = _store.Members.Any(m => phones.Contains(m.Phone, StringComparer.Ordinal));
            return Task.FromResult(exists);
        }

        public Task AddRangeAsync(IReadOnlyList<Member> members, CancellationToken ct)
        {
            _store.Members.AddRange(members);
            return Task.CompletedTask;
        }

        public Task<List<string>> GenerateIdsAsync(int count, CancellationToken ct)
        {
            var ids = Enumerable.Range(1, count)
                .Select(_ => $"C{Guid.NewGuid():N}"[..7].ToUpperInvariant())
                .ToList();
            return Task.FromResult(ids);
        }
    }

    internal sealed class InMemoryStudentProfileRepository : IStudentProfileRepository
    {
        private readonly InMemoryStore _store;

        public InMemoryStudentProfileRepository(InMemoryStore store)
        {
            _store = store;
        }

        public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct)
        {
            _store.Profiles.AddRange(profiles);
            return Task.CompletedTask;
        }

        public Task UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct)
        {
            var profile = _store.Profiles.FirstOrDefault(x => x.UserId == userId);
            if (profile is not null)
            {
                profile.UpdateCurrentTicket(snapshot);
            }

            return Task.CompletedTask;
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

    internal sealed class SystemClock : IClock
    {
        public DateTime Now() => DateTime.UtcNow;
        public DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
