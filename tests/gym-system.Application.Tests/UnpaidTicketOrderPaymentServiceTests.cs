using gym_system.Application.OrdersUseCase.Services;
using gym_system.Application.TicketPlansUseCase.Queries;
using gym_system.Domain.Entities.Members;
using gym_system.Domain.Entities.Orders;
using gym_system.Domain.Entities.Tickets;
using gym_system.Domain.Entities.Users;
using gym_system.Domain.Enums;
using gym_system.Domain.Exceptions;
using gym_system.Domain.Repositories;
using Xunit;

namespace gym_system.Application.Tests;

public sealed class UnpaidTicketOrderPaymentServiceTests
{
    private static readonly DateTime PaidAt = new(2026, 9, 3, 14, 30, 0);

    [Fact]
    public async Task PayAsync_ShouldUseActualPaidAtToCreateAndActivatePass()
    {
        var fixture = new Fixture();

        await fixture.Service.PayAsync("ORD-1", "Cash", "ADMIN-1");

        Assert.Equal(PaidAt, fixture.OrderRepository.MarkedPaidAt);
        var pass = Assert.Single(fixture.PassRepository.Passes);
        Assert.Equal(PaidAt, pass.PaidAt);
        Assert.Equal(TicketValidStatus.Active, pass.ValidStatus);
        Assert.Equal(new DateOnly(2026, 9, 3), pass.ValidStartDate);
        Assert.Equal(new DateOnly(2026, 10, 2), pass.ValidEndDate);
        Assert.NotNull(fixture.ProfileRepository.Profile.CurrentTicket);
    }

    [Fact]
    public async Task PayAsync_ShouldRejectWhenCurrentPriceDiffersFromOrderSnapshot()
    {
        var fixture = new Fixture(planPrice: 2000m);

        var error = await Assert.ThrowsAsync<TicketPurchaseRejectedException>(() =>
            fixture.Service.PayAsync("ORD-1", "Cash", "ADMIN-1"));

        Assert.Equal("TICKET_PLAN_PRICE_CHANGED", error.Code);
        Assert.Contains("價格已變更", error.Message);
        Assert.Null(fixture.OrderRepository.MarkedPaidAt);
        Assert.Empty(fixture.PassRepository.Passes);
    }

    [Fact]
    public async Task PayAsync_ShouldRevalidateEligibilityBeforeMarkingOrderPaid()
    {
        var fixture = new Fixture(canPurchase: false);

        var error = await Assert.ThrowsAsync<TicketPurchaseRejectedException>(() =>
            fixture.Service.PayAsync("ORD-1", "Cash", "ADMIN-1"));

        Assert.Equal("TICKET_PLAN_NOT_AVAILABLE", error.Code);
        Assert.Contains("資格已失效", error.Message);
        Assert.Null(fixture.OrderRepository.MarkedPaidAt);
        Assert.Empty(fixture.PassRepository.Passes);
    }

    private sealed class Fixture
    {
        public Fixture(decimal planPrice = 1960m, bool canPurchase = true,
            bool newOnlyAtPayment = false, DateTime? assignedAt = null, bool hasPass = false)
        {
            var plan = new TicketPlanKind
            {
                Id = "MONTHLY",
                Name = "月票",
                FamilyCode = "MONTHLY",
                Type = TicketPlanType.MPass,
                Price = planPrice,
                DefaultExpireDays = 30,
                IsActive = true
            };
            OrderRepository = new FakeOrderRepository();
            PassRepository = new FakeTicketPassRepository();
            ProfileRepository = new FakeStudentProfileRepository();
            var roles = new FakeUserRoleRepository(assignedAt ?? PaidAt.AddMonths(-1));
            ITicketPlanEligibilityService eligibility = newOnlyAtPayment
                ? new TicketPlanEligibilityService(ProfileRepository, roles, new FakeClock(),
                    [new NewOnlyTicketPlanEligibilityRule(new FakePurchaseHistory(hasPass))])
                : new FakeEligibilityService(canPurchase);
            Service = new UnpaidTicketOrderPaymentService(
                new FakeUserRepository(),
                roles,
                ProfileRepository,
                new FakeTicketPlanRepository(plan),
                new FakeTicketPlanCatalogQueryService(plan, newOnlyAtPayment),
                eligibility,
                new RenewalTicketPassEligibilityService(PassRepository),
                OrderRepository,
                PassRepository,
                new FakeClock());
        }

        public UnpaidTicketOrderPaymentService Service { get; }
        public FakeOrderRepository OrderRepository { get; }
        public FakeTicketPassRepository PassRepository { get; }
        public FakeStudentProfileRepository ProfileRepository { get; }
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public DateTime? MarkedPaidAt { get; private set; }

        public Task<UnpaidTicketOrder?> FindUnpaidTicketOrderAsync(
            string orderId,
            bool acquireLock,
            CancellationToken ct) => Task.FromResult<UnpaidTicketOrder?>(new UnpaidTicketOrder
            {
                OrderSn = 1,
                OrderId = "ORD-1",
                BuyerId = "U1",
                OrderItemSn = 2,
                OrderItemId = "ITEM-1",
                TicketPlanKindCode = "MONTHLY",
                Quantity = 1,
                UnitPrice = 1960m,
                TotalAmount = 1960m,
                ActualAmount = 1960m
            });

        public Task MarkPaidAsync(
            int orderSn,
            int orderItemSn,
            DateTime paidAt,
            string paymentMethod,
            string operatorId,
            CancellationToken ct)
        {
            MarkedPaidAt = paidAt;
            return Task.CompletedTask;
        }

        public Task<OrderPersistenceResult> AddAsync(Order order, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTicketPassRepository : ITicketPassRepository
    {
        public List<TicketPass> Passes { get; } = [];

        public Task<IReadOnlyList<TicketPassPersistenceResult>> AddRangeAsync(
            IReadOnlyList<TicketPass> passes,
            OrderPersistenceResult orderPersistence,
            CancellationToken ct)
        {
            Passes.AddRange(passes);
            IReadOnlyList<TicketPassPersistenceResult> result = passes
                .Select((pass, index) => new TicketPassPersistenceResult
                {
                    ClientPassId = pass.Id,
                    OwnerId = pass.OwnerId,
                    PassSn = index + 1,
                    PassId = pass.Id
                })
                .ToList();
            return Task.FromResult(result);
        }

        public Task LockOwnerAsync(string ownerId, CancellationToken ct) => Task.CompletedTask;

        public Task<RenewalSourcePass?> FindLatestRenewalSourceAsync(
            string ownerId,
            string familyCode,
            bool acquireLock,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> HasQueuedPassAsync(string ownerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CancelledTicketPassResult?> CancelQueuedRenewalAsync(
            string passId,
            DateTime cancelledAt,
            string operatorId,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<CurrentTicketSnapshot?> ReconcileCurrentAsync(
            string ownerId,
            DateOnly today,
            DateTime updatedAt,
            string operatorId,
            CancellationToken ct)
        {
            var pass = Assert.Single(Passes);
            pass.Activate(today);
            return Task.FromResult<CurrentTicketSnapshot?>(pass.ToSnapshot());
        }
    }

    private sealed class FakeStudentProfileRepository : IStudentProfileRepository
    {
        public StudentProfile Profile { get; } = StudentProfile.Create("U1");

        public Task<StudentProfile?> FindByUserIdAsync(string userId, CancellationToken ct) =>
            Task.FromResult<StudentProfile?>(Profile);

        public Task<bool> UpdateCurrentTicketAsync(
            string userId,
            CurrentTicketSnapshot snapshot,
            CancellationToken ct)
        {
            Profile.UpdateCurrentTicket(snapshot);
            return Task.FromResult(true);
        }

        public Task<bool> ClearCurrentTicketAsync(string userId, DateTime updatedAt, CancellationToken ct) =>
            Task.FromResult(true);
        public Task AddAsync(StudentProfile profile, CancellationToken ct) => throw new NotSupportedException();
        public Task AddRangeAsync(IReadOnlyList<StudentProfile> profiles, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateLastVisitAsync(string userId, DateTime lastVisitAt, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public Task<User?> FindUserByIdAsync(string userId, CancellationToken ct) =>
            Task.FromResult<User?>(User.Rehydrate("U1", "User", "0900000000", "pwd", true));
        public Task<IReadOnlyList<string>> GetExistingPhonesAsync(IReadOnlyList<string> phones, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindUserByPhoneAsync(string phone, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsPhoneForOtherUserAsync(string userId, string phone, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> UpdateBasicProfileAsync(string userId, string name, string phone, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUserRoleRepository(DateTime assignedAt) : IUserRoleRepository
    {
        public Task<UserRole?> GetUserRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct) =>
            Task.FromResult<UserRole?>(UserRole.Assign("U1", UserRoleCode.Student, assignedAt, true));
        public Task<IReadOnlyList<UserRole>> GetActiveRolesAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> AddRoleAsync(UserRole userRole, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ReactivateRoleAsync(string userId, UserRoleCode roleType, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> SetRoleActiveAsync(string userId, UserRoleCode roleType, bool isActive, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeTicketPlanRepository : ITicketPlanRepository
    {
        private readonly TicketPlanKind _plan;
        public FakeTicketPlanRepository(TicketPlanKind plan) => _plan = plan;
        public Task<TicketPlanKind?> GetActiveByIdAsync(string ticketPlanKindId, CancellationToken ct) =>
            Task.FromResult<TicketPlanKind?>(_plan);
    }

    private sealed class FakeTicketPlanCatalogQueryService : ITicketPlanCatalogQueryService
    {
        private readonly TicketPlanKind _plan;
        private readonly bool _newOnly;
        public FakeTicketPlanCatalogQueryService(TicketPlanKind plan, bool newOnly)
        {
            _plan = plan;
            _newOnly = newOnly;
        }
        public Task<IReadOnlyList<TicketPlanResult>> GetActiveTicketPlansAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TicketPlanResult>>(
            [
                new TicketPlanResult
                {
                    Id = _plan.Id,
                    Name = _plan.Name,
                    FamilyCode = _plan.FamilyCode,
                    Type = "MONTHLY",
                    Price = _plan.Price,
                    Days = _plan.DefaultExpireDays,
                    Sessions = _plan.DefaultCredit,
                    EligibilityRuleCodes = _newOnly ? ["NEW_ONLY"] : []
                }
            ]);
    }

    private sealed class FakeEligibilityService : ITicketPlanEligibilityService
    {
        private readonly bool _canPurchase;
        public FakeEligibilityService(bool canPurchase) => _canPurchase = canPurchase;

        public Task<StudentTicketPlanEligibilityContext?> GetEligibilityContextAsync(
            string studentId,
            CancellationToken ct) => Task.FromResult<StudentTicketPlanEligibilityContext?>(
            new StudentTicketPlanEligibilityContext
            {
                StudentId = studentId,
                Kind = TicketPlanEligibilityContextKind.ExistingMember,
                IsActiveStudent = true,
                Now = PaidAt
            });

        public StudentTicketPlanEligibilityContext CreateRegistrationContext() =>
            new()
            {
                Kind = TicketPlanEligibilityContextKind.Registration,
                IsActiveStudent = false,
                Now = PaidAt
            };

        public Task<bool> CanPurchaseAsync(
            StudentTicketPlanEligibilityContext context,
            TicketPlanResult ticketPlan,
            CancellationToken ct) => Task.FromResult(_canPurchase);
    }

    private sealed class FakeClock : IClock
    {
        public DateTime Now() => PaidAt;
        public DateOnly Today() => DateOnly.FromDateTime(PaidAt);
    }

    // E12: an existing unpaid order is evaluated as an existing member using
    // the current catalog, even when NEW_ONLY was added after order creation.
    [Fact]
    public async Task DelayedPayment_ShouldUseExistingMemberRulesAndIssuePassOnlyAfterPayment()
    {
        var fixture = new Fixture(newOnlyAtPayment: true, assignedAt: new DateTime(2026, 9, 1));
        Assert.Empty(fixture.PassRepository.Passes);
        Assert.Null(fixture.ProfileRepository.Profile.CurrentTicket);

        await fixture.Service.PayAsync("ORD-1", "Cash", "ADMIN-1");

        var pass = Assert.Single(fixture.PassRepository.Passes);
        Assert.Equal(PaidAt, pass.PaidAt);
        Assert.Equal(DateOnly.FromDateTime(PaidAt), pass.ValidStartDate);
        Assert.Equal(TicketValidStatus.Active, pass.ValidStatus);
    }

    // E13: a valid order does not reserve eligibility until a later payment.
    [Theory]
    [InlineData(30, false)]
    [InlineData(2, true)]
    public async Task DelayedPayment_ShouldRejectExpiredOrConsumedNewOnlyEligibility(
        int daysSinceAssignment, bool hasPass)
    {
        var fixture = new Fixture(newOnlyAtPayment: true,
            assignedAt: PaidAt.AddDays(-daysSinceAssignment), hasPass: hasPass);

        var error = await Assert.ThrowsAsync<TicketPurchaseRejectedException>(
            () => fixture.Service.PayAsync("ORD-1", "Cash", "ADMIN-1"));

        Assert.Equal("TICKET_PLAN_NOT_AVAILABLE", error.Code);
        Assert.Null(fixture.OrderRepository.MarkedPaidAt);
        Assert.Empty(fixture.PassRepository.Passes);
        Assert.Null(fixture.ProfileRepository.Profile.CurrentTicket);
    }

    private sealed class FakePurchaseHistory(bool hasPass) : IStudentTicketPurchaseHistoryQueryService
    {
        public Task<bool> HasPurchasedTicketPlanAsync(string studentId, string code, CancellationToken ct)
            => Task.FromResult(hasPass);
    }
}
