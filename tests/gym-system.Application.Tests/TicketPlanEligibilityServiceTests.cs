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
        public async Task GetEligibilityContextAsync_ShouldReturnContext_WhenStudentIsActive()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 20));

            var actual = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);

            Assert.NotNull(actual);
            Assert.True(actual.IsActiveStudent);
            Assert.Equal(new DateTime(2026, 6, 20), actual.StudentAssignedAt);
            Assert.Equal("U0000000001", actual.StudentId);
        }

        [Fact]
        public async Task CanPurchaseAsync_ShouldReturnTrue_ForRegularPlan_WhenStudentIsActive()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 1));
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);
            var plan = CreatePlan(eligibilityRuleCodes: []);

            var actual = await fixture.Service.CanPurchaseAsync(context!, plan,
            CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnTrue_ForNewOnlyPlan_WhenStudentJoinedWithinThirtyDays()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 20));
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);
            var plan = CreatePlan(eligibilityRuleCodes: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync(context!, plan,
            CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnTrue_ForNewOnlyPlan_OnThirtiethCalendarDay()
        {
            var fixture = CreateFixture(
                assignedAt: new DateTime(2026, 6, 11, 23, 59, 0),
                now: new DateTime(2026, 7, 10, 0, 1, 0));
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);
            var plan = CreatePlan(eligibilityRuleCodes: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync(context!, plan,
            CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnFalse_ForNewOnlyPlan_OnThirtyFirstCalendarDay()
        {
            var fixture = CreateFixture(
                assignedAt: new DateTime(2026, 6, 10, 23, 59, 0),
                now: new DateTime(2026, 7, 10, 0, 1, 0));
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);
            var plan = CreatePlan(eligibilityRuleCodes: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync(context!, plan,
            CancellationToken.None);

            Assert.False(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnFalse_ForNewOnlyPlan_WhenStudentJoinedMoreThanThirtyDaysAgo()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 1));
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);
            var plan = CreatePlan(eligibilityRuleCodes: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync(context!, plan,
            CancellationToken.None);

            Assert.False(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnTrue_ForRegularPlan_InRegistrationContext()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 20));
            var context = fixture.Service.CreateRegistrationContext();
            var plan = CreatePlan(eligibilityRuleCodes: []);

            var actual = await fixture.Service.CanPurchaseAsync(context, plan,
            CancellationToken.None);

            Assert.True(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnFalse_ForNewOnlyPlan_InRegistrationContext()
        {
            var fixture = CreateFixture(assignedAt: new DateTime(2026, 6, 20));
            var context = fixture.Service.CreateRegistrationContext();
            var plan = CreatePlan(eligibilityRuleCodes: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync(context, plan,
            CancellationToken.None);

            Assert.False(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnFalse_ForNewOnlyPlan_WhenStudentAlreadyPurchasedSamePlan()
        {
            var fixture = CreateFixture(
                assignedAt: new DateTime(2026, 6, 20),
                purchasedPlanCodes: ["FREE_TRIAL"]);
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);
            var plan = CreatePlan(eligibilityRuleCodes: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync(context!, plan,
            CancellationToken.None);

            Assert.False(actual);
        }

        [Fact]
        public async Task
        CanPurchaseAsync_ShouldReturnFalse_ForNewOnlyPlan_WhenCanceledPassExists()
        {
            var fixture = CreateFixture(
                assignedAt: new DateTime(2026, 6, 20),
                canceledPlanCodes: ["FREE_TRIAL"]);
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);
            var plan = CreatePlan(eligibilityRuleCodes: ["NEW_ONLY"]);

            var actual = await fixture.Service.CanPurchaseAsync(context!, plan,
            CancellationToken.None);

            Assert.False(actual);
        }

        [Fact]
        public async Task GetEligibilityContextAsync_ShouldReturnNull_WhenStudentRoleIsInactive()
        {
            var fixture = CreateFixture(
                assignedAt: new DateTime(2026, 6, 20),
                isRoleActive: false);

            var actual = await fixture.Service.GetEligibilityContextAsync("U0000000001",
            CancellationToken.None);

            Assert.Null(actual);
        }

        // E02: registration must reject both current membership-only rules.
        [Theory]
        [InlineData("NEW_ONLY")]
        [InlineData("RENEWAL")]
        public async Task Registration_ShouldRejectRuleWithoutExplicitOptIn(string ruleCode)
        {
            var rule = new DefaultRegistrationRule(ruleCode);
            var fixture = CreateFixture(new DateTime(2026, 9, 1), rules: [rule]);

            var allowed = await fixture.Service.CanPurchaseAsync(
                fixture.Service.CreateRegistrationContext(), CreatePlan([ruleCode]), CancellationToken.None);

            Assert.False(allowed);
            Assert.False(rule.WasEvaluated);
        }

        // E03/E04: registration support is only a gate, not the eligibility result.
        [Theory]
        [InlineData(true, true, true, true)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(false, true, true, false)]
        public async Task Registration_ShouldCheckSupportApplicabilityAndCondition(
            bool supportsRegistration, bool applies, bool satisfied, bool expected)
        {
            var rule = new ConfigurableRule("REGISTRATION_OFFER", supportsRegistration, applies, satisfied);
            var fixture = CreateFixture(new DateTime(2026, 9, 1), rules: [rule]);
            var context = fixture.Service.CreateRegistrationContext();

            var allowed = await fixture.Service.CanPurchaseAsync(
                context, CreatePlan([rule.RuleCode]), CancellationToken.None);

            Assert.Equal(expected, allowed);
            Assert.Null(context.StudentId);
            Assert.False(context.IsActiveStudent);
            Assert.Equal(supportsRegistration && applies, rule.WasEvaluated);
        }

        [Fact]
        public async Task Registration_ShouldRejectPlanWhenOneRuleDoesNotSupportRegistration()
        {
            var supported = new ConfigurableRule("REGISTRATION_OFFER", true, true, true);
            var memberOnly = new DefaultRegistrationRule("MEMBER_ONLY");
            var fixture = CreateFixture(new DateTime(2026, 9, 1), rules: [supported, memberOnly]);

            var allowed = await fixture.Service.CanPurchaseAsync(
                fixture.Service.CreateRegistrationContext(),
                CreatePlan([supported.RuleCode, memberOnly.RuleCode]), CancellationToken.None);

            Assert.False(allowed);
            Assert.False(memberOnly.WasEvaluated);
        }

        // E05: unknown codes emitted by the catalog must never become unrestricted plans.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CanPurchase_ShouldRejectUnknownRuleInEitherContext(bool registration)
        {
            var fixture = CreateFixture(new DateTime(2026, 9, 1));
            var context = registration
                ? fixture.Service.CreateRegistrationContext()
                : await fixture.Service.GetEligibilityContextAsync("U0000000001", CancellationToken.None);

            var allowed = await fixture.Service.CanPurchaseAsync(
                context!, CreatePlan(["UNKNOWN_RESTRICTION"]), CancellationToken.None);

            Assert.False(allowed);
        }

        [Fact]
        public async Task ExistingMember_ShouldRunMemberOnlyRuleAndRejectFailedCondition()
        {
            var rule = new ConfigurableRule("MEMBER_ONLY", false, true, false);
            var fixture = CreateFixture(new DateTime(2026, 9, 1), rules: [rule]);
            var context = await fixture.Service.GetEligibilityContextAsync("U0000000001", CancellationToken.None);

            Assert.False(await fixture.Service.CanPurchaseAsync(
                context!, CreatePlan([rule.RuleCode]), CancellationToken.None));
            Assert.True(rule.WasEvaluated);
        }

        private sealed class DefaultRegistrationRule(string ruleCode) : ITicketPlanEligibilityRule
        {
            public string RuleCode => ruleCode;
            public bool WasEvaluated { get; private set; }
            public bool AppliesTo(TicketPlanResult plan) => true;
            public Task<bool> IsSatisfiedAsync(StudentTicketPlanEligibilityContext context,
                TicketPlanResult plan, CancellationToken ct)
            {
                WasEvaluated = true;
                return Task.FromResult(true);
            }
        }

        private sealed class ConfigurableRule(string ruleCode, bool supportsRegistration,
            bool applies, bool satisfied) : ITicketPlanEligibilityRule
        {
            public string RuleCode => ruleCode;
            public bool SupportsRegistration => supportsRegistration;
            public bool WasEvaluated { get; private set; }
            public bool AppliesTo(TicketPlanResult plan) => applies;
            public Task<bool> IsSatisfiedAsync(StudentTicketPlanEligibilityContext context,
                TicketPlanResult plan, CancellationToken ct)
            {
                WasEvaluated = true;
                return Task.FromResult(satisfied);
            }
        }

        private static Fixture CreateFixture(
            DateTime assignedAt,
            bool isRoleActive = true,
            DateTime? now = null,
            string[]? purchasedPlanCodes = null,
            string[]? canceledPlanCodes = null,
            ITicketPlanEligibilityRule[]? rules = null)
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
            var purchaseHistoryQueryService = new FakeStudentTicketPurchaseHistoryQueryService(
                purchasedPlanCodes ?? [],
                canceledPlanCodes ?? []);
            rules ??= new ITicketPlanEligibilityRule[]
            {
                  new NewOnlyTicketPlanEligibilityRule(purchaseHistoryQueryService)
            };

            return new Fixture(
                new TicketPlanEligibilityService(
                    profileRepository,
                    userRoleRepository,
                    clock,
                    rules));
        }

        private static TicketPlanResult CreatePlan(string[] eligibilityRuleCodes)
        {
            return new TicketPlanResult
            {
                Id = "FREE_TRIAL",
                Name = "免費體驗票",
                Price = 0,
                Days = 30,
                Sessions = 1,
                Type = "SESSION",
                Tags = [],
                EligibilityRuleCodes = eligibilityRuleCodes,
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

            public Task AddAsync(StudentProfile profile, CancellationToken ct) =>
            Task.CompletedTask;
            public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken
            ct) => Task.CompletedTask;
            public Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct) =>
            Task.FromResult(Profile);
            public Task<bool> UpdateLastVisitAsync(string userId, DateTime lastVisitAt,
            CancellationToken ct) => Task.FromResult(true);
            public Task<bool> UpdateCurrentTicketAsync(string userId, CurrentTicketSnapshot
            snapshot, CancellationToken ct) => Task.FromResult(true);
            public Task<bool> ClearCurrentTicketAsync(string userId, DateTime updatedAt,
            CancellationToken ct) => Task.FromResult(true);
        }

        private sealed class FakeUserRoleRepository : IUserRoleRepository
        {
            public UserRole? Role { get; init; }

            public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType,
            CancellationToken ct)
                => Task.FromResult(Role);

            public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId,
            CancellationToken ct)
                => Task.FromResult<IReadOnlyList<UserRole>>(Role is null ? [] : [Role]);

            public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct) =>
            Task.FromResult(true);
            public Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType,
            CancellationToken ct) => Task.FromResult(true);
            public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool
            isActive, CancellationToken ct) => Task.FromResult(true);
        }

        private sealed class FakeStudentTicketPurchaseHistoryQueryService :
        IStudentTicketPurchaseHistoryQueryService
        {
            private readonly HashSet<string> _purchasedPlanCodes;
            private readonly HashSet<string> _canceledPlanCodes;

            public FakeStudentTicketPurchaseHistoryQueryService(
                IEnumerable<string> purchasedPlanCodes,
                IEnumerable<string> canceledPlanCodes)
            {
                _purchasedPlanCodes = new HashSet<string>(purchasedPlanCodes,
                StringComparer.OrdinalIgnoreCase);
                _canceledPlanCodes = new HashSet<string>(canceledPlanCodes,
                StringComparer.OrdinalIgnoreCase);
            }

            public Task<bool> HasPurchasedTicketPlanAsync(
                string studentId,
                string ticketPlanCode,
                CancellationToken ct)
            {
                if (_canceledPlanCodes.Contains(ticketPlanCode))
                {
                    return Task.FromResult(true);
                }

                return Task.FromResult(_purchasedPlanCodes.Contains(ticketPlanCode));
            }
        }
    }
}
