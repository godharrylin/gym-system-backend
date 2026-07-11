using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests
{
    public sealed class TicketPlanEligibilityServiceTests
    {
        [Fact]
        public async Task CanPurchaseAsync_ShouldReturnTrue_ForRegularPlan_WhenStudentIsActive()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 1));
            var plan = CreatePlan(tags: []);

            var actual = await fixture.Service.CanPurchaseAsync("U0000000001", plan, CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task CanPurchaseAsync_ShouldReturnTrue_ForNewOnlyPlan_WhenStudentJoinedWithinThirtyDays()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 20));
            var plan = CreatePlan(tags: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync("U0000000001", plan, CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task CanPurchaseAsync_ShouldReturnFalse_ForNewOnlyPlan_WhenStudentJoinedMoreThanThirtyDaysAgo()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 1));
            var plan = CreatePlan(tags: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync("U0000000001", plan, CancellationToken.None);

            Assert.False(actual);
        }

        [Fact]
        public async Task CanPurchaseAsync_ShouldReturnFalse_WhenStudentRoleIsInactive()
        {
            var fixture = CreateFixture(
                assignedAt: new DateTime(2026, 6, 20),
                isRoleActive: false);
            var plan = CreatePlan(tags: []);

            var actual = await fixture.Service.CanPurchaseAsync("U0000000001", plan, CancellationToken.None);

            Assert.False(actual);
        }

        private static Fixture CreateFixture(
            DateTime assignedAt,
            bool isRoleActive = true,
            DateTime? now = null)
        {
            var studentId = "U0000000001";
            var profileRepository = new FakeStudentProfileRepository
            {
                Profile = StudentProfile.Create(studentId)
            };
            var userRoleRepository = new FakeUserRoleRepository
            {
                Role = UserRole.Assign(studentId, UserRoleCode.Student, assignedAt, isRoleActive)
            };
            var clock = new FakeClock(now ?? new DateTime(2026, 7, 10));

            return new Fixture(
                new TicketPlanEligibilityService(profileRepository, userRoleRepository, clock));
        }

        private static TicketPlanResult CreatePlan(string[] tags)
        {
            return new TicketPlanResult
            {
                Id = "FREE_TRIAL",
                Name = "免費體驗票",
                Price = 0,
                Days = 30,
                Sessions = 1,
                Type = "SESSION",
                Tags = tags,
                Description = null
            };
        }

        private sealed record Fixture(TicketPlanEligibilityService Service);

        private sealed class FakeClock : IClock
        {
            private readonly DateTime _now;

            public FakeClock(DateTime now)
            {
                _now = now;
            }

            public DateTime Now() => _now;
            public DateOnly Today() => DateOnly.FromDateTime(_now);
        }

        private sealed class FakeStudentProfileRepository : IStudentProfileRepository
        {
            public StudentProfile? Profile { get; init; }

            public Task AddAsync(StudentProfile profile, CancellationToken ct) => Task.CompletedTask;
            public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct) => Task.CompletedTask;
            public Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct) => Task.FromResult(Profile);
            public Task<bool> UpdateLastVisitAsync(string userId, DateTime lastVisitAt, CancellationToken ct) => Task.FromResult(true);
            public Task<bool> UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot snapshot, CancellationToken ct) => Task.FromResult(true);
        }

        private sealed class FakeUserRoleRepository : IUserRoleRepository
        {
            public UserRole? Role { get; init; }

            public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct)
                => Task.FromResult(Role);

            public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct)
                => Task.FromResult<IReadOnlyList<UserRole>>(Role is null ? [] : [Role]);

            public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct) => Task.FromResult(true);
            public Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct) => Task.FromResult(true);
            public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct) => Task.FromResult(true);
        }
    }
}
